<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use App\Models\Share;
use App\Models\ShareImage;
use App\Support\Share\Screenshots;
use App\Support\Share\ShareError;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Http\Response;
use Illuminate\Http\UploadedFile;

/**
 * Screenshots of the signed-in player's listing, for clients such as Borea: list, add (one per
 * request, multipart field `image`), and remove. Each change goes to the index by pull request.
 */
class ShareImageController extends Controller
{
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
