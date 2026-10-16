// Connects a host and two guests to a signaling room on the Worker and checks
// that messages pass between them. Usage: node test.mjs [base-url]
import WebSocket from "ws";

const base = (process.argv[2] ?? "http://localhost:8799").replace(/^http/, "ws");
const code = "TEST" + Math.random().toString(36).slice(2, 8).toUpperCase();

function open(role) {
	const ws = new WebSocket(`${base}/room/${code}?role=${role}`);
	const inbox = [];
	ws.on("message", (m) => inbox.push(JSON.parse(m.toString())));
	return new Promise((resolve, reject) => {
		ws.on("open", () => resolve({ ws, inbox }));
		ws.on("unexpected-response", (_, res) => reject(new Error(`${role}: HTTP ${res.statusCode}`)));
		ws.on("error", reject);
	});
}

const until = async (check) => {
	for (let i = 0; i < 50 && !check(); i++) await new Promise((r) => setTimeout(r, 100));
	if (!check()) throw new Error("timed out");
};
const find = (box, type) => box.inbox.find((m) => m.type === type);

try {
	await open("guest");
	throw new Error("guest joined a room with no host");
} catch (e) {
	if (!e.message.includes("404")) throw e;
}

const host = await open("host");
const a = await open("guest");
const b = await open("guest");
await until(() => find(a, "welcome") && find(b, "welcome"));
const idA = find(a, "welcome").id, idB = find(b, "welcome").id;
await until(() => host.inbox.filter((m) => m.type === "guest-joined").length === 2);

a.ws.send(JSON.stringify({ type: "offer", sdp: "from a" }));
await until(() => host.inbox.some((m) => m.type === "offer" && m.from === idA));
host.ws.send(JSON.stringify({ type: "answer", sdp: "to b", to: idB }));
await until(() => find(b, "answer"));
if (find(a, "answer")) throw new Error("answer reached the wrong guest");

a.ws.close();
await until(() => host.inbox.some((m) => m.type === "guest-left" && m.from === idA));
host.ws.close();
await until(() => b.ws.readyState >= WebSocket.CLOSING);
console.log(`ok (${code})`);
process.exit(0);
