// Two players in a matchmaking lobby on the Worker: create, search, join,
// data both ways, joinable, the owner leaving, and a lobby that leaves after
// sitting idle long enough to hibernate. Usage: node lobby-test.mjs [base-url]
import WebSocket from "ws";

const base = (process.argv[2] ?? "http://localhost:8799").replace(/\/$/, "");
const ws = base.replace(/^http/, "ws");
let lobbyId = String(Math.floor(Math.random() * 2 ** 50));
const tag = "t" + lobbyId;

function open(member, name, create) {
	const socket = new WebSocket(`${ws}/lobby/${lobbyId}?member=${member}&name=${name}${create ? "&create=1&max=2" : ""}`);
	const inbox = [];
	socket.on("message", (m) => inbox.push(JSON.parse(m.toString())));
	return new Promise((resolve, reject) => {
		socket.on("open", () => resolve({ socket, inbox }));
		socket.on("unexpected-response", (_, res) => reject(new Error(`HTTP ${res.statusCode}`)));
		socket.on("error", reject);
	});
}
const until = async (check, what) => {
	for (let i = 0; i < 50 && !check(); i++) await new Promise((r) => setTimeout(r, 100));
	if (!check()) throw new Error("timed out: " + what);
};
const has = (box, f) => box.inbox.some(f);
const search = async () => (await (await fetch(`${base}/lobbies?eq.test=${tag}`)).json()).lobbies;
const status = async (member) => open(member, "x").then(({ socket }) => { socket.close(); return 101; }, (e) => parseInt(e.message.slice(5)));

if ((await status("9")) !== 404) throw new Error("joined a lobby that doesn't exist");
const a = await open("111", "Alice", true);
await until(() => has(a, (m) => m.type === "state" && m.owner === "111"), "state for the owner");
a.socket.send(JSON.stringify({ type: "data", key: "test", value: tag }));
await until(() => has(a, (m) => m.type === "data" && m.values?.test === tag), "data echo");
// Many at once, as a lobby is described
a.socket.send(JSON.stringify({ type: "data", values: { one: "1", two: "2" } }));
await until(() => has(a, (m) => m.type === "data" && m.values?.one === "1" && m.values?.two === "2"), "batched data");
await new Promise((r) => setTimeout(r, 300));
const found = await search();
if (found.length !== 1 || found[0].id !== lobbyId || found[0].members !== 1) throw new Error("search: " + JSON.stringify(found));

const b = await open("222", "Bob", false);
await until(() => has(b, (m) => m.type === "state" && m.data.test === tag && m.data.two === "2" && m.members.length === 2), "state for the guest");
await until(() => has(a, (m) => m.type === "entered" && m.member === "222" && m.name === "Bob"), "entered");
b.socket.send(JSON.stringify({ type: "data", key: "test", value: "hijack" }));
b.socket.send(JSON.stringify({ type: "memberData", key: "picks", value: "{}" }));
await until(() => has(a, (m) => m.type === "memberData" && m.member === "222" && m.key === "picks"), "member data");
if (has(a, (m) => m.type === "data" && m.values?.test === "hijack")) throw new Error("a guest wrote lobby data");
if ((await search()).length !== 0) throw new Error("a full lobby is still found");
if ((await status("333")) !== 409) throw new Error("joined a full lobby");

a.socket.close();
await until(() => has(b, (m) => m.type === "left" && m.member === "111" && m.owner === "222"), "owner passes on");
b.socket.send(JSON.stringify({ type: "joinable", value: false }));
await new Promise((r) => setTimeout(r, 300));
if ((await status("333")) !== 403) throw new Error("joined a lobby that isn't joinable");
b.socket.send(JSON.stringify({ type: "joinable", value: true }));
await new Promise((r) => setTimeout(r, 300));
if ((await search()).length !== 1) throw new Error("the lobby with room again isn't found");
b.socket.close();
await new Promise((r) => setTimeout(r, 500));
if ((await search()).length !== 0) throw new Error("an empty lobby is still found");
if ((await status("9")) !== 404) throw new Error("an empty lobby can still be joined");

// Idle long enough to hibernate: it wakes up for the close and still has to delist itself
lobbyId = String(Math.floor(Math.random() * 2 ** 50));
const idle = await open("111", "Alice", true);
idle.socket.send(JSON.stringify({ type: "data", key: "test", value: tag }));
await new Promise((r) => setTimeout(r, 300));
if ((await search()).length !== 1) throw new Error("the idle lobby isn't found");
await new Promise((r) => setTimeout(r, 20000));
idle.socket.close();
await new Promise((r) => setTimeout(r, 1000));
if ((await search()).length !== 0) throw new Error("a lobby that hibernated is still found after it emptied");
console.log("ok");
process.exit(0);
