// Connects a host and two guests to the signaling Worker and checks that
// messages pass between them. Usage: node test.mjs [base-url]
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
// The directory of hosted games
const http = base.replace(/^ws/, "http");
const id = "test-" + code.toLowerCase();
const put = (key, body) => fetch(`${http}/listings/${id}`, { method: "PUT", headers: { "X-Listing-Key": key }, body: JSON.stringify(body) });
const listed = async () => (await (await fetch(`${http}/listings`)).json()).listings.find((l) => l.id === id);
const key = "k".repeat(24);
if ((await put(key, { platform: "epic", joinCode: "ABC234", data: { "bb.match": "1" } })).status !== 204) throw new Error("put failed");
const got = await listed();
if (got?.platform !== "epic" || got.joinCode !== "ABC234" || got.data["bb.match"] !== "1" || got.key) throw new Error("listing wrong: " + JSON.stringify(got));
if ((await put("x".repeat(24), { joinCode: "HIJACK", data: {} })).status !== 403) throw new Error("someone else changed the listing");
await fetch(`${http}/listings/${id}`, { method: "DELETE", headers: { "X-Listing-Key": key } });
if (await listed()) throw new Error("listing still there");

console.log(`ok (${code})`);
process.exit(0);
