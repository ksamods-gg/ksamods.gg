<?php

namespace App\Http\Controllers;

use App\Models\Share;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

/**
 * The GitHub App's webhook: when a listing pull request the bot opened is merged or closed, the
 * listing remembers it and its owner hears about it.
 */
class GitHubWebhookController extends Controller
{
    public function __invoke(Request $request): JsonResponse
    {
        $secret = (string) config('share.github_app.webhook_secret');
        $signature = 'sha256='.hash_hmac('sha256', $request->getContent(), $secret);

        // Anyone can post here; only GitHub knows the secret.
        abort_if($secret === '' || ! hash_equals($signature, (string) $request->header('X-Hub-Signature-256')), 401);

        if ($request->header('X-GitHub-Event') !== 'pull_request' || $request->input('action') !== 'closed') {
            return response()->json(['ignored' => true]);
        }

        $share = Share::where('pull_request_url', (string) $request->input('pull_request.html_url'))->first();

        if ($share === null || $share->pull_request_state !== 'open') {
            return response()->json(['ignored' => true]);
        }

        $merged = (bool) $request->input('pull_request.merged');
        $share->forceFill(['pull_request_state' => $merged ? 'merged' : 'closed'])->save();
        $merged ? $share->notifyOwner('share_merged') : $share->notifyOwner('share_closed', url: $share->pull_request_url);

        return response()->json(['state' => $share->pull_request_state]);
    }
}
