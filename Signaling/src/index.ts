// WebRTC signaling for BeaverBuddies. Each join code is a room backed by one
// Durable Object. The host keeps a WebSocket open on /room/<code>?role=host for
// as long as it hosts; each joining player opens one with role=guest for the
// few seconds it takes to connect. The room gives every guest an id and passes
// messages (offer, answer, ICE candidates) between it and the host: a guest's
// messages reach the host with "from" set to the guest's id, and the host
// addresses a guest with "to".
//
// /listings is the directory of hosted games, for the players who can't see
// Steam lobbies (the Epic Games build, or anyone with Steam networking off):
// every host puts its game there, Steam or not, and puts it again every 30
// seconds while it's open. A listing nobody refreshes for 90 seconds is gone.
//
// /lobby/<id> is a matchmaking lobby (see lobby.ts), and /lobbies searches
// the ones open to join.

import { DurableObject } from "cloudflare:workers";
import { Lobby, type LobbySummary } from "./lobby";

export { Lobby };

export interface Env {
	ROOMS: DurableObjectNamespace<Room>;
	DIRECTORY: DurableObjectNamespace<Directory>;
	LOBBIES: DurableObjectNamespace<Lobby>;
}

const CODE = /^[A-Z0-9]{4,12}$/;
const MAX_MESSAGE = 16 * 1024;
// A guest that hasn't connected by then never will
const GUEST_LIFETIME = 2 * 60 * 1000;

export default {
	async fetch(request: Request, env: Env): Promise<Response> {
		const url = new URL(request.url);
		if (url.pathname === "/listings" || url.pathname.startsWith("/listings/") || url.pathname === "/lobbies") {
			return env.DIRECTORY.get(env.DIRECTORY.idFromName("directory")).fetch(request);
		}
		const lobby = url.pathname.match(/^\/lobby\/([0-9]{1,20})$/);
		if (lobby) {
			if (request.headers.get("Upgrade") !== "websocket") {
				return new Response("Expected a WebSocket", { status: 426 });
			}
			return env.LOBBIES.get(env.LOBBIES.idFromName(lobby[1])).fetch(request);
		}
		const match = url.pathname.match(/^\/room\/([^/]+)$/);
		if (!match) {
			return new Response("BeaverBuddies signaling", { status: 404 });
		}
		const code = match[1].toUpperCase();
		if (!CODE.test(code)) {
			return new Response("Bad room code", { status: 400 });
		}
		if (request.headers.get("Upgrade") !== "websocket") {
			return new Response("Expected a WebSocket", { status: 426 });
		}
		const role = url.searchParams.get("role");
		if (role !== "host" && role !== "guest") {
			return new Response("role must be host or guest", { status: 400 });
		}
		const room = env.ROOMS.get(env.ROOMS.idFromName(code));
		return room.fetch(request);
	},
};

export class Room extends DurableObject<Env> {
	async fetch(request: Request): Promise<Response> {
		const role = new URL(request.url).searchParams.get("role")!;
		const pair = new WebSocketPair();

		if (role === "host") {
			// A reconnecting host replaces its old socket
			for (const old of this.ctx.getWebSockets("host")) {
				old.close(4000, "Replaced");
			}
			this.ctx.acceptWebSocket(pair[1], ["host"]);
		} else {
			if (this.ctx.getWebSockets("host").length === 0) {
				return new Response("No host with that code", { status: 404 });
			}
			const id = crypto.randomUUID().slice(0, 8);
			this.ctx.acceptWebSocket(pair[1], ["guest", "guest:" + id]);
			pair[1].serializeAttachment({ id, until: Date.now() + GUEST_LIFETIME });
			pair[1].send(JSON.stringify({ type: "welcome", id }));
			this.toHost({ type: "guest-joined", from: id });
			await this.ctx.storage.setAlarm(Date.now() + GUEST_LIFETIME);
		}
		return new Response(null, { status: 101, webSocket: pair[0] });
	}

	async webSocketMessage(ws: WebSocket, message: string | ArrayBuffer) {
		if (typeof message !== "string" || message.length > MAX_MESSAGE) {
			return;
		}
		let parsed: { to?: string; [key: string]: unknown };
		try {
			parsed = JSON.parse(message);
		} catch {
			return;
		}
		if (this.ctx.getTags(ws).includes("host")) {
			if (typeof parsed.to !== "string") return;
			for (const guest of this.ctx.getWebSockets("guest:" + parsed.to)) {
				guest.send(message);
			}
		} else {
			const { id } = ws.deserializeAttachment() as { id: string };
			this.toHost({ ...parsed, from: id });
		}
	}

	async webSocketClose(ws: WebSocket, code: number) {
		// Completes the closing handshake the other side started
		try {
			ws.close(code === 1005 ? 1000 : code, "Bye");
		} catch {}
		if (this.ctx.getTags(ws).includes("host")) {
			// A host that reconnected replaced this socket: the room carries on with the new one
			if (this.ctx.getWebSockets("host").some((other) => other !== ws)) return;
			for (const guest of this.ctx.getWebSockets("guest")) {
				guest.close(4002, "Host left");
			}
		} else {
			const { id } = ws.deserializeAttachment() as { id: string };
			this.toHost({ type: "guest-left", from: id });
		}
	}

	// Drops guests that are still here long after joining
	async alarm() {
		let next = Infinity;
		for (const ws of this.ctx.getWebSockets("guest")) {
			const { until } = ws.deserializeAttachment() as { until: number };
			if (until <= Date.now()) ws.close(4001, "Took too long");
			else next = Math.min(next, until);
		}
		if (next < Infinity) await this.ctx.storage.setAlarm(next);
	}

	private toHost(message: object) {
		for (const ws of this.ctx.getWebSockets("host")) {
			ws.send(JSON.stringify(message));
		}
	}
}

const LISTING_LIFETIME = 90 * 1000;
const MAX_LISTING = 64 * 1024;
const MAX_LISTINGS = 500;
const ID = /^[A-Za-z0-9-]{8,64}$/;

interface Listing {
	id: string;
	// steam, epic or other: the icon beside the host's name
	platform: string;
	// For WebRTC (see Room)
	joinCode: string;
	// The game's Steam lobby, when the host has one: Steam players join through it
	steamLobby?: string;
	// The lobby data's keys and values (MatchKeys in BeaverBuddies)
	data: Record<string, string>;
	until: number;
}

// A lobby nobody relisted for this long is gone (see lobby.ts, which relists every minute)
const LOBBY_LIFETIME = 150 * 1000;

export class Directory extends DurableObject<Env> {
	private listings = new Map<string, Listing & { key: string }>();
	// Matchmaking lobbies, in memory only: each relists itself every minute
	private lobbies = new Map<string, LobbySummary & { until: number }>();
	private loaded = false;

	// From the lobbies themselves (see lobby.ts), never from outside
	async putLobby(summary: LobbySummary) {
		this.lobbies.set(summary.id, { ...summary, until: Date.now() + LOBBY_LIFETIME });
	}

	async deleteLobby(id: string) {
		this.lobbies.delete(id);
	}

	/**
	 * The lobbies anyone can join: with room, joinable, and with every
	 * eq.<key>=<value> of the query in their data. most=<n> caps how many.
	 */
	private searchLobbies(url: URL): Response {
		const now = Date.now();
		const equal: [string, string][] = [];
		for (const [param, value] of url.searchParams) {
			if (param.startsWith("eq.")) equal.push([param.slice(3), value]);
		}
		const most = Math.min(Math.max(parseInt(url.searchParams.get("most") ?? "50") || 50, 1), 100);
		const found: LobbySummary[] = [];
		for (const [id, lobby] of this.lobbies) {
			if (lobby.until <= now) {
				this.lobbies.delete(id);
				continue;
			}
			if (!lobby.joinable || lobby.members >= lobby.max) continue;
			if (!equal.every(([key, value]) => lobby.data[key] === value)) continue;
			const { until, ...summary } = lobby;
			found.push(summary);
			if (found.length >= most) break;
		}
		return Response.json({ lobbies: found }, { headers: { "Cache-Control": "no-store" } });
	}

	async fetch(request: Request): Promise<Response> {
		await this.load();
		if (new URL(request.url).pathname === "/lobbies") {
			if (request.method !== "GET") return new Response("Use GET", { status: 405 });
			return this.searchLobbies(new URL(request.url));
		}
		// Read before any answer: one sent with the body unread breaks the connection for the next request
		const body = request.method === "PUT" ? await request.text() : "";
		const url = new URL(request.url);
		const id = url.pathname.split("/")[2];
		const now = Date.now();
		for (const [key, listing] of this.listings) {
			if (listing.until <= now) {
				this.listings.delete(key);
				this.ctx.storage.delete(key);
			}
		}

		if (request.method === "GET" && !id) {
			const open = [...this.listings.values()].map(({ key, until, ...listing }) => listing);
			return Response.json({ listings: open }, { headers: { "Cache-Control": "no-store" } });
		}
		if (!id || !ID.test(id)) {
			return new Response("Bad listing id", { status: 400 });
		}
		// Whoever first put a listing owns it: only their key changes or removes it
		const key = request.headers.get("X-Listing-Key") ?? "";
		const existing = this.listings.get(id);
		if (key.length < 16 || (existing && existing.key !== key)) {
			return new Response("Not your listing", { status: 403 });
		}

		if (request.method === "DELETE") {
			this.listings.delete(id);
			await this.ctx.storage.delete(id);
			return new Response(null, { status: 204 });
		}
		if (request.method !== "PUT") {
			return new Response("Use GET, PUT or DELETE", { status: 405 });
		}
		if (body.length > MAX_LISTING) {
			return new Response("Listing too big", { status: 413 });
		}
		if (!existing && this.listings.size >= MAX_LISTINGS) {
			return new Response("Directory full", { status: 503 });
		}
		let parsed: Partial<Listing>;
		try {
			parsed = JSON.parse(body);
		} catch {
			return new Response("Bad JSON", { status: 400 });
		}
		if (typeof parsed.joinCode !== "string" || typeof parsed.data !== "object" || parsed.data === null) {
			return new Response("A listing needs joinCode and data", { status: 400 });
		}
		const listing = {
			id,
			key,
			platform: typeof parsed.platform === "string" ? parsed.platform.slice(0, 16) : "other",
			joinCode: parsed.joinCode.slice(0, 16),
			steamLobby: typeof parsed.steamLobby === "string" ? parsed.steamLobby.slice(0, 24) : undefined,
			data: Object.fromEntries(Object.entries(parsed.data).filter(([, v]) => typeof v === "string")) as Record<string, string>,
			until: now + LISTING_LIFETIME,
		};
		this.listings.set(id, listing);
		await this.ctx.storage.put(id, listing);
		return new Response(null, { status: 204 });
	}

	// Back from storage after the object was evicted
	private async load() {
		if (this.loaded) return;
		this.loaded = true;
		const stored = await this.ctx.storage.list<Listing & { key: string }>();
		for (const [id, listing] of stored) this.listings.set(id, listing);
	}
}
