// A matchmaking lobby, for the players who can't use Steam's: what Quick match
// needs of one (see BeaverBuddies' Matchmaking/Lobbies). Each member keeps a
// WebSocket open on /lobby/<id>?member=<id>&name=<name>, the first with
// &create=1&max=<n>. Like a Steam lobby it has an owner, data only the owner
// writes, data each member writes for themselves, and a switch for whether
// anyone else may join; every change goes to every member. When the owner
// leaves, the member who came in next owns it; when the last one leaves, it's
// gone. It lists itself in the Directory, which search reads.
//
// Ids are decimal strings: they're 64-bit, more than a JSON number holds.

import { DurableObject } from "cloudflare:workers";
import type { Env } from "./index";

const ID = /^[0-9]{1,20}$/;
const MAX_MESSAGE = 64 * 1024;
const MAX_KEYS = 64;
// Listed again this often, so the directory can drop a lobby whose object went away
const RELIST_EVERY = 60 * 1000;

interface Member {
	id: string;
	name: string;
	data: Record<string, string>;
	joined: number;
}

interface State {
	owner: string;
	max: number;
	joinable: boolean;
	data: Record<string, string>;
}

export interface LobbySummary {
	id: string;
	owner: string;
	members: number;
	max: number;
	joinable: boolean;
	data: Record<string, string>;
}

export class Lobby extends DurableObject<Env> {
	private state: State | null = null;
	private loaded = false;

	private async load() {
		if (this.loaded) return;
		this.loaded = true;
		this.state = (await this.ctx.storage.get<State>("state")) ?? null;
	}

	private async save() {
		if (this.state) await this.ctx.storage.put("state", this.state);
	}

	private members(): { ws: WebSocket; member: Member }[] {
		return this.ctx
			.getWebSockets()
			.map((ws) => ({ ws, member: ws.deserializeAttachment() as Member }))
			.filter((m) => m.member)
			.sort((a, b) => a.member.joined - b.member.joined);
	}

	async fetch(request: Request): Promise<Response> {
		await this.load();
		const url = new URL(request.url);
		const lobbyId = url.pathname.split("/")[2];
		const member = url.searchParams.get("member") ?? "";
		const name = (url.searchParams.get("name") ?? "").slice(0, 64);
		if (!ID.test(lobbyId) || !ID.test(member)) {
			return new Response("Bad lobby or member id", { status: 400 });
		}
		const present = this.members();

		if (url.searchParams.get("create") === "1") {
			if (this.state && present.length > 0) return new Response("Lobby exists", { status: 409 });
			const max = Math.min(Math.max(parseInt(url.searchParams.get("max") ?? "2") || 2, 1), 16);
			this.state = { owner: member, max, joinable: true, data: {} };
		} else {
			if (!this.state || present.length === 0) return new Response("No such lobby", { status: 404 });
			const rejoining = present.some((m) => m.member.id === member);
			if (!rejoining) {
				if (!this.state.joinable) return new Response("Not joinable", { status: 403 });
				if (present.length >= this.state.max) return new Response("Full", { status: 409 });
			}
		}

		// The same player again (back after a restart, say) replaces their old socket
		for (const old of present.filter((m) => m.member.id === member)) {
			old.ws.serializeAttachment(null);
			old.ws.close(4000, "Replaced");
		}

		const pair = new WebSocketPair();
		const joined: Member = { id: member, name, data: {}, joined: Date.now() };
		this.ctx.acceptWebSocket(pair[1]);
		pair[1].serializeAttachment(joined);
		await this.save();

		pair[1].send(
			JSON.stringify({
				type: "state",
				id: lobbyId,
				owner: this.state.owner,
				max: this.state.max,
				joinable: this.state.joinable,
				data: this.state.data,
				members: this.members().map((m) => m.member),
			}),
		);
		this.broadcast({ type: "entered", member, name }, pair[1]);
		await this.list(lobbyId);
		await this.ctx.storage.setAlarm(Date.now() + RELIST_EVERY);
		return new Response(null, { status: 101, webSocket: pair[0] });
	}

	async webSocketMessage(ws: WebSocket, message: string | ArrayBuffer) {
		await this.load();
		const member = ws.deserializeAttachment() as Member | null;
		if (!member || !this.state || typeof message !== "string" || message.length > MAX_MESSAGE) return;
		let parsed: { type?: string; key?: unknown; value?: unknown; values?: unknown };
		try {
			parsed = JSON.parse(message);
		} catch {
			return;
		}
		const key = typeof parsed.key === "string" ? parsed.key.slice(0, 128) : null;
		const value = typeof parsed.value === "string" ? parsed.value : null;
		const owner = member.id === this.state.owner;
		switch (parsed.type) {
			case "data": {
				// One key, or many at once ("values"), so a lobby is described in one write and one listing
				if (!owner) return;
				const values: Record<string, string> = {};
				if (key !== null && value !== null) values[key] = value;
				if (typeof parsed.values === "object" && parsed.values !== null) {
					for (const [k, v] of Object.entries(parsed.values)) if (typeof v === "string") values[k.slice(0, 128)] = v;
				}
				for (const [k, v] of Object.entries(values)) {
					if (!(k in this.state.data) && Object.keys(this.state.data).length >= MAX_KEYS) continue;
					this.state.data[k] = v;
				}
				await this.save();
				this.broadcast({ type: "data", values });
				break;
			}
			case "memberData":
				if (key === null || value === null) return;
				if (!(key in member.data) && Object.keys(member.data).length >= MAX_KEYS) return;
				member.data[key] = value;
				ws.serializeAttachment(member);
				this.broadcast({ type: "memberData", member: member.id, key, value });
				// Members' own data isn't listed
				return;
			case "joinable":
				if (!owner || typeof parsed.value !== "boolean") return;
				this.state.joinable = parsed.value;
				await this.save();
				break;
			default:
				return;
		}
		await this.list(this.lobbyId(ws));
	}

	async webSocketClose(ws: WebSocket, code: number) {
		try {
			ws.close(code === 1005 ? 1000 : code, "Bye");
		} catch {}
		await this.gone(ws);
	}

	async webSocketError(ws: WebSocket) {
		await this.gone(ws);
	}

	private async gone(ws: WebSocket) {
		await this.load();
		const member = ws.deserializeAttachment() as Member | null;
		if (!member || !this.state) return;
		ws.serializeAttachment(null);
		const id = this.lobbyId(ws);
		const left = this.members().filter((m) => m.ws !== ws);
		if (left.length === 0) {
			await this.ctx.storage.deleteAll();
			this.state = null;
			if (id) await this.directory().deleteLobby(id);
			return;
		}
		if (member.id === this.state.owner) this.state.owner = left[0].member.id;
		await this.save();
		this.broadcast({ type: "left", member: member.id, owner: this.state.owner });
		await this.list(id);
	}

	async alarm() {
		await this.load();
		const present = this.members();
		if (!this.state || present.length === 0) return;
		await this.list(this.storedId);
		await this.ctx.storage.setAlarm(Date.now() + RELIST_EVERY);
	}

	private storedId: string | null = null;

	// The lobby's id, which the object only knows from the requests that reach it
	private lobbyId(_ws?: WebSocket): string | null {
		return this.storedId;
	}

	private directory() {
		return this.env.DIRECTORY.get(this.env.DIRECTORY.idFromName("directory"));
	}

	private async list(id: string | null) {
		if (id) {
			if (this.storedId !== id) await this.ctx.storage.put("id", id);
			this.storedId = id;
		} else {
			id = this.storedId ?? (await this.ctx.storage.get<string>("id")) ?? null;
			this.storedId = id;
		}
		if (!id || !this.state) return;
		const summary: LobbySummary = {
			id,
			owner: this.state.owner,
			members: this.members().length,
			max: this.state.max,
			joinable: this.state.joinable,
			data: this.state.data,
		};
		await this.directory().putLobby(summary);
	}

	private broadcast(message: object, except?: WebSocket) {
		const text = JSON.stringify(message);
		for (const { ws } of this.members()) {
			if (ws === except) continue;
			try {
				ws.send(text);
			} catch {}
		}
	}
}
