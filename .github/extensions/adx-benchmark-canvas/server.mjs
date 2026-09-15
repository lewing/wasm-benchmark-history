import { createServer } from "node:http";
import { renderHtml } from "./renderer.mjs";

const MAX_REQUEST_BYTES = 1_000_000;

function json(res, status, value) {
    res.writeHead(status, {
        "Content-Type": "application/json; charset=utf-8",
        "Cache-Control": "no-store",
        "X-Content-Type-Options": "nosniff",
        "Content-Security-Policy": "default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'",
    });
    res.end(JSON.stringify(value));
}

async function readJson(req) {
    let size = 0;
    const chunks = [];
    for await (const chunk of req) {
        size += chunk.length;
        if (size > MAX_REQUEST_BYTES) {
            const error = new Error("Request body exceeds the 1 MB limit");
            error.code = "oversized_input";
            throw error;
        }
        chunks.push(chunk);
    }
    if (chunks.length === 0) {
        return {};
    }
    try {
        return JSON.parse(Buffer.concat(chunks).toString("utf8"));
    } catch {
        const error = new Error("Request body must be valid JSON");
        error.code = "invalid_json";
        throw error;
    }
}

export async function startCanvasServer(handlers) {
    const clients = new Set();
    const server = createServer(async (req, res) => {
        try {
            const url = new URL(req.url ?? "/", "http://127.0.0.1");
            if (req.method === "GET" && url.pathname === "/") {
                res.writeHead(200, {
                    "Content-Type": "text/html; charset=utf-8",
                    "Cache-Control": "no-store",
                    "X-Content-Type-Options": "nosniff",
                    "Content-Security-Policy": "default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'",
                });
                res.end(renderHtml());
                return;
            }
            if (req.method === "GET" && url.pathname === "/api/state") {
                json(res, 200, await handlers.getState());
                return;
            }
            if (req.method === "GET" && url.pathname === "/events") {
                res.writeHead(200, {
                    "Content-Type": "text/event-stream",
                    "Cache-Control": "no-cache",
                    "Connection": "keep-alive",
                    "X-Accel-Buffering": "no",
                });
                res.write(": connected\n\n");
                clients.add(res);
                req.on("close", () => clients.delete(res));
                return;
            }
            const routes = new Map([
                ["/api/view", handlers.setView],
                ["/api/select", handlers.select],
                ["/api/ask", handlers.ask],
                ["/api/export", handlers.export],
            ]);
            if (req.method === "POST" && routes.has(url.pathname)) {
                const result = await routes.get(url.pathname)(await readJson(req));
                json(res, 200, result ?? {});
                return;
            }
            json(res, 404, { code: "not_found", message: "Canvas endpoint not found" });
        } catch (error) {
            json(res, 400, {
                code: error?.code ?? "canvas_request_failed",
                message: error instanceof Error ? error.message : String(error),
            });
        }
    });
    await new Promise((resolve, reject) => {
        server.once("error", reject);
        server.listen(0, "127.0.0.1", resolve);
    });
    const address = server.address();
    const port = typeof address === "object" && address ? address.port : 0;
    return {
        server,
        url: `http://127.0.0.1:${port}/`,
        broadcast(state) {
            const payload = `event: state\ndata: ${JSON.stringify(state)}\n\n`;
            for (const client of clients) {
                client.write(payload);
            }
        },
        async close() {
            for (const client of clients) {
                client.end();
            }
            clients.clear();
            await new Promise((resolve) => server.close(() => resolve()));
        },
    };
}
