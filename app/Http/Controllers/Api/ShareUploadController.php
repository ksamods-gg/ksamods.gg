<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use App\Models\ShareUpload;
use App\Support\Share\ShareError;
use App\Support\Share\ShareHost;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Http\UploadedFile;
use Illuminate\Support\Str;
use Laravel\Passport\AccessToken;
use Laravel\Passport\Passport;

/**
 * The upload API of rule 15, at `api` in hosts.toml. Answers and errors are JSON; a ShareError
 * becomes `{ "error", "message" }` with its status (rule 16).
 */
class ShareUploadController extends Controller
{
    public function store(Request $request): JsonResponse
    {
        $archive = $request->file('archive');

        if (! $archive instanceof UploadedFile || ! $archive->isValid()) {
            throw new ShareError('invalid_request', 'Send the archive as the multipart field archive.', 400);
        }

        $upload = ShareHost::upload(
            $request->user(),
            $archive,
            (string) $request->input('dependencies', ''),
            $request->filled('listing') ? (string) $request->input('listing') : null,
            $this->clientName($request),
        );

        return response()->json(ShareHost::answer($upload), 201);
    }

    /**
     * The name the signed-in client gave at sign-in, such as borea, kept with the upload.
     */
    private function clientName(Request $request): ?string
    {
        $token = $request->user()->token();
        $client = $token instanceof AccessToken ? Passport::client()->newQuery()->find($token->oauth_client_id) : null;

        return $client === null ? null : Str::after($client->name, ShareDeviceController::PREFIX);
    }

    public function publish(Request $request, string $upload): JsonResponse
    {
        $found = ShareUpload::find($upload) ?? throw new ShareError('upload_expired', 'There is no such upload; it may have expired. Upload the archive again.', 404);

        return response()->json(ShareHost::publish($request->user(), $found, $request->all()), 201);
    }
}
