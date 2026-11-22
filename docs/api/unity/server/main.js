// Nakama JS runtime module — bootstraps the demo content the Unity sample talks to.
// Mounted read-only at /nakama/data/modules (Nakama's default --runtime.path).

function InitModule(ctx, logger, nk, initializer) {
	var LEADERBOARDS = [
		{ id: "global_score", authoritative: false, sort: "descending", operator: "best", reset: "0 0 * * 1", metadata: { title: "Global high score", reset: "weekly" } },
		{ id: "level_speedrun", authoritative: false, sort: "ascending", operator: "best", reset: "", metadata: { title: "Fastest level clear (ms)", reset: "never" } }
	];

	LEADERBOARDS.forEach(function (lb) {
		try {
			nk.leaderboardCreate(lb.id, lb.authoritative, lb.sort, lb.operator, lb.reset, lb.metadata, null);
			logger.info("leaderboard created: %s", lb.id);
		} catch (e) {
			// already exists on every restart after the first — that is the normal path
			logger.info("leaderboard present: %s (%s)", lb.id, e);
		}
	});

	initializer.registerRpc("server_time", rpcServerTime);
	initializer.registerRpc("daily_bonus", rpcDailyBonus);
	initializer.registerRpc("submit_and_echo", rpcSubmitAndEcho);
	logger.info("nakama-unity-sample module loaded");
}

// Tiny health/echo RPC so the Unity sample can prove RPC wiring without game logic.
function rpcServerTime(ctx, logger, nk, payload) {
	return JSON.stringify({ server: "nakama-3.42", now: new Date().toISOString() });
}

// Server-authoritative wallet grant: client asks, server decides the amount.
function rpcDailyBonus(ctx, logger, nk, payload) {
	if (!ctx.userId) {
		throw "authentication required";
	}
	var body = {};
	try { body = JSON.parse(payload || "{}"); } catch (e) { body = {}; }
	var amount = 100;
	var result = nk.walletUpdate(ctx.userId, { coins: amount }, { source: "daily_bonus", client: body.reason || "" }, true);
	return JSON.stringify({ granted: amount, wallet: result.updated || {}, delta: result.delta || {} });
}

// Echoes what the client sent — useful to confirm the Authorization header and JSON plumbing.
function rpcSubmitAndEcho(ctx, logger, nk, payload) {
	return JSON.stringify({ userId: ctx.userId, username: ctx.username, received: payload || "" });
}
