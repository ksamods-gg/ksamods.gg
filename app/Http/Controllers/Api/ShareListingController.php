<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use App\Models\Share;
use App\Models\ShareImage;
use App\Support\Share\Screenshots;
use App\Support\Share\ShareError;
use App\Support\Share\ShareHost;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Http\Response;
use Illuminate\Http\UploadedFile;

/**
 * The signed-in player's own listing, for clients such as Borea, as on its page on the site:
 * change its text, take a version down, and list, add (one per request, multipart field `image`)
 * and remove its screenshots. Each change of the listing goes to the index by pull request.
 */
class ShareListingController extends Controller
{
    /**
     * Counts a download, for the site's download buttons and for clients that install a listing.
     * No sign-in: a player counts once a day per listing, however often this is called. The
     * optional `version` says which release; without it, or for a version not up, the newest
     * release still up gets the download.
     */
    public function download(Request $request, string $type, string $id): Response
    {
        $share = Share::where('type', $type)->where('listing_id', $id)->firstOrFail();
        $releases = $share->releases()->reorder()->whereNull('taken_down_at')->latest('id');
        $release = $releases->clone()->where('version', (string) $request->input('version'))->first()
            ?? $releases->first()
            ?? abort(404);
        $share->countDownload((string) $request->ip(), $release);

        return response()->noContent();
    }

    /**
     * Changes the listing's text: any of name, abstract, description, license and tags; the
     * others stay as they are. Proposed to the index like an edit on the site.
     */
    public function update(Request $request, string $type, string $id): JsonResponse
    {
        $share = $this->owned($request, $type, $id);
        $input = [
            'name' => $share->name,
            'abstract' => $share->abstract,
            'description' => $share->description,
            'license' => $share->license,
            'tags' => $share->tags,
            ...$request->only(['name', 'abstract', 'description', 'license', 'tags']),
        ];

        return response()->json([
            'id' => $share->listing_id,
            'type' => $share->type,
            'pull_request' => ShareHost::amend($share, $input),
            'page' => $share->url(),
        ]);
    }

    /**
     * Takes one of the listing's versions down, as on the site: its download answers 410 and it
     * leaves the release list. It cannot be undone, and the version is never given out again.
     */
    public function takeDown(Request $request, string $type, string $id, string $version): JsonResponse
    {
        $share = $this->owned($request, $type, $id);
        $release = $share->releases()->where('version', $version)->first()
            ?? throw new ShareError('invalid_request', "{$id} has no version {$version}.", 404);

        ShareHost::takeDown($release, $request->user());

        return response()->json(['id' => $share->listing_id, 'type' => $share->type, 'version' => $release->version, 'taken_down' => true]);
    }

    public function index(Request $request, string $type, string $id): JsonResponse
    {
        return response()->json(['images' => $this->records($this->owned($request, $type, $id))]);
    }

    public function store(Request $request, string $type, string $id): JsonResponse
    {
        $share = $this->owned($request, $type, $id);
        $file = $request->file('image');

        if (! $file instanceof UploadedFile || ! $file->isValid()) {
            throw new ShareError('invalid_request', 'Send the screenshot as the multipart field image.', 400);
        }

        Screenshots::add($share, (string) $file->get());

        return response()->json(['images' => $this->records($share)], 201);
    }

    public function destroy(Request $request, string $type, string $id, int $image): Response
    {
        $share = $this->owned($request, $type, $id);
        Screenshots::remove($share->images()->findOr($image, fn () => throw new ShareError('invalid_request', 'The listing has no such screenshot.', 404)));

        return response()->noContent();
    }

    /**
     * @throws ShareError
     */
    private function owned(Request $request, string $type, string $id): Share
    {
        $share = Share::where('type', $type)->where('listing_id', $id)->first()
            ?? throw new ShareError('not_owner', "There is no {$type} {$id} on this host.", 404);

        if ($share->user_id !== $request->user()->id) {
            throw new ShareError('not_owner', "Your account does not own {$id}.", 403);
        }

        return $share;
    }

    /**
     * @return list<array{id: int, url: string, width: int, height: int, cover: bool}>
     */
    private function records(Share $share): array
    {
        return array_values($share->images()->get()->map(fn (ShareImage $image) => [
            'id' => $image->id,
            'url' => $image->url(),
            'width' => $image->width,
            'height' => $image->height,
            'cover' => $image->position === 0,
        ])->all());
    }
}
