import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import path from "node:path";
import { randomUUID } from "node:crypto";
import {
    CONTRACT_VERSION,
    normalizeAnalysis,
    normalizeChartSpec,
    normalizeSelection,
    validateAnalysisId,
} from "./schema.mjs";

export function createEmptyState(analysisId) {
    validateAnalysisId(analysisId);
    return {
        version: CONTRACT_VERSION,
        analysisId,
        analysis: null,
        view: normalizeChartSpec({ visualization: "time-series" }),
        selection: null,
        revision: 0,
        updatedAt: new Date(0).toISOString(),
    };
}

export function shouldImportArtifact(state) {
    return state.analysis === null;
}

export class AnalysisMutationQueue {
    constructor() {
        this.chains = new Map();
    }

    async run(analysisId, operation) {
        const previous = this.chains.get(analysisId) ?? Promise.resolve();
        const current = previous.catch(() => undefined).then(operation);
        this.chains.set(analysisId, current);
        try {
            return await current;
        } finally {
            if (this.chains.get(analysisId) === current) {
                this.chains.delete(analysisId);
            }
        }
    }
}

export class AnalysisStore {
    constructor(workspacePath) {
        if (!workspacePath) {
            throw new Error("The Copilot session workspace is unavailable");
        }
        this.workspacePath = path.resolve(workspacePath);
        this.basePath = path.join(this.workspacePath, "files", "adx-benchmark-canvas", "analyses");
    }

    async load(analysisId) {
        validateAnalysisId(analysisId);
        try {
            const raw = JSON.parse(await readFile(this.#statePath(analysisId), "utf8"));
            return this.#normalizeState(raw, analysisId);
        } catch (error) {
            if (error?.code === "ENOENT") {
                return createEmptyState(analysisId);
            }
            throw error;
        }
    }

    async save(state) {
        const normalized = this.#normalizeState(state, state.analysisId);
        normalized.revision = Number.isInteger(state.revision) && state.revision >= 0
            ? state.revision
            : 0;
        normalized.updatedAt = new Date().toISOString();
        await mkdir(this.basePath, { recursive: true });
        const destination = this.#statePath(normalized.analysisId);
        const temporary = `${destination}.${randomUUID()}.tmp`;
        await writeFile(temporary, `${JSON.stringify(normalized, null, 2)}\n`, {
            encoding: "utf8",
            mode: 0o600,
        });
        await rename(temporary, destination);
        return normalized;
    }

    async readArtifact(relativePath) {
        if (typeof relativePath !== "string" || relativePath.length === 0 || relativePath.length > 512) {
            const error = new Error("artifactPath must be a non-empty session-relative path");
            error.code = "invalid_artifact_path";
            throw error;
        }
        if (path.isAbsolute(relativePath) || relativePath.includes("\0")) {
            const error = new Error("artifactPath must be session-relative");
            error.code = "invalid_artifact_path";
            throw error;
        }
        const resolved = path.resolve(this.workspacePath, relativePath);
        if (resolved !== this.workspacePath && !resolved.startsWith(`${this.workspacePath}${path.sep}`)) {
            const error = new Error("artifactPath escapes the session workspace");
            error.code = "invalid_artifact_path";
            throw error;
        }
        const statPath = path.relative(this.workspacePath, resolved);
        if (statPath.startsWith("..")) {
            const error = new Error("artifactPath escapes the session workspace");
            error.code = "invalid_artifact_path";
            throw error;
        }
        return normalizeAnalysis(JSON.parse(await readFile(resolved, "utf8")));
    }

    stateArtifactPath(analysisId) {
        validateAnalysisId(analysisId);
        return path.relative(this.workspacePath, this.#statePath(analysisId));
    }

    exportPath(analysisId, extension) {
        validateAnalysisId(analysisId);
        return path.join(this.workspacePath, "files", "adx-benchmark-canvas", "exports", `${analysisId}.${extension}`);
    }

    async writeExport(analysisId, extension, content) {
        const destination = this.exportPath(analysisId, extension);
        await mkdir(path.dirname(destination), { recursive: true });
        await writeFile(destination, content, { encoding: "utf8", mode: 0o600 });
        return path.relative(this.workspacePath, destination);
    }

    #statePath(analysisId) {
        return path.join(this.basePath, `${analysisId}.json`);
    }

    #normalizeState(value, expectedAnalysisId) {
        if (value === null || typeof value !== "object" || Array.isArray(value)) {
            throw new Error("Stored analysis state must be an object");
        }
        const analysisId = validateAnalysisId(value.analysisId ?? expectedAnalysisId);
        if (analysisId !== expectedAnalysisId) {
            throw new Error("Stored analysis ID does not match its durable key");
        }
        const analysis = value.analysis === null || value.analysis === undefined
            ? null
            : normalizeAnalysis(value.analysis);
        if (analysis && analysis.analysisId !== analysisId) {
            throw new Error("Stored analysis payload uses a different analysis ID");
        }
        return {
            version: CONTRACT_VERSION,
            analysisId,
            analysis,
            view: normalizeChartSpec(value.view ?? analysis?.chart ?? { visualization: "time-series" }),
            selection: normalizeSelection(value.selection),
            revision: Number.isInteger(value.revision) && value.revision >= 0 ? value.revision : 0,
            updatedAt: typeof value.updatedAt === "string" ? value.updatedAt : new Date(0).toISOString(),
        };
    }
}
