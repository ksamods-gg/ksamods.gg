<?php

namespace App\Support\Share;

use App\Models\Share;
use App\Models\ShareImage;
use GdImage;
use Illuminate\Support\Str;

/**
 * Screenshots of a vehicle or save (RFC 0058 images): up to six, each resized to fit 1920px and
 * stored as WebP on the share disk; the first is the cover, whose square crop is the listing's
 * icon. Every change goes to the index in the listing document, by pull request.
 */
class Screenshots
{
    public const MAX = 6;

    // What an upload may be before it is converted.
    public const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;

    // RFC 0058: a description image is at most 1 MiB, an icon at most 256 KiB and 256 to 2048px.
    private const MAX_IMAGE_BYTES = 1048576;

    private const MAX_ICON_BYTES = 262144;

    /**
     * @throws ShareError
     */
    public static function add(Share $share, string $bytes): ShareImage
    {
        if ($share->images()->count() >= self::MAX) {
            throw new ShareError('invalid_request', 'A listing has at most '.self::MAX.' screenshots. Remove one first.', 422);
        }

        if (strlen($bytes) > self::MAX_UPLOAD_BYTES) {
            throw new ShareError('too_large', 'A screenshot can be at most 10 MB.', 413);
        }

        $image = @imagecreatefromstring($bytes);

        if (! $image instanceof GdImage) {
            throw new ShareError('invalid_request', 'The screenshot must be a PNG, JPEG or WebP image.', 422);
        }

        $image = self::fit($image, 1920);
        $webp = self::webp($image, self::MAX_IMAGE_BYTES)
            ?? throw new ShareError('invalid_request', 'The screenshot is too detailed to fit 1 MB as WebP. Try a smaller one.', 422);

        $path = "images/{$share->type}/{$share->listing_id}/".Str::random(16).'.webp';
        DataFiles::disk()->put($path, $webp, ['visibility' => 'public', 'ContentType' => 'image/webp']);

        $screenshot = $share->images()->create([
            'position' => (int) $share->images()->max('position') + ($share->images()->exists() ? 1 : 0),
            'path' => $path,
            'sha256' => hash('sha256', $webp),
            'width' => imagesx($image),
            'height' => imagesy($image),
            'size' => strlen($webp),
        ]);

        self::changed($share);

        return $screenshot;
    }

    /**
     * Removes a screenshot, and its file at once: a moderator's removal must not wait for the index.
     */
    public static function remove(ShareImage $screenshot): void
    {
        $share = $screenshot->share;
        DataFiles::disk()->delete($screenshot->path);
        $screenshot->delete();
        self::renumber($share->images()->get());
        self::changed($share);
    }

    public static function makeCover(ShareImage $screenshot): void
    {
        $share = $screenshot->share;
        self::renumber($share->images()->get()->sortBy(fn (ShareImage $image) => $image->is($screenshot) ? -1 : $image->position));
        self::changed($share);
    }

    /**
     * @param  iterable<ShareImage>  $images  In their new order.
     */
    private static function renumber(iterable $images): void
    {
        $position = 0;

        foreach ($images as $image) {
            $image->forceFill(['position' => $position++])->save();
        }
    }

    /**
     * Cuts the icon from the cover again, and proposes the listing with its new images.
     */
    private static function changed(Share $share): void
    {
        $cover = $share->images()->first();
        $icon = null;

        $source = $cover === null ? false : @imagecreatefromstring((string) DataFiles::disk()->get($cover->path));

        // Without a readable cover there is no icon to cut.
        if ($source instanceof GdImage) {
            $side = min(imagesx($source), imagesy($source));
            $square = imagecreatetruecolor(512, 512);
            imagecopyresampled($square, $source, 0, 0, intdiv(imagesx($source) - $side, 2), intdiv(imagesy($source) - $side, 2), 512, 512, $side, $side);
            $webp = self::webp($square, self::MAX_ICON_BYTES);

            if ($webp !== null) {
                // A new name each time: the index keeps reading the old icon until the change merges.
                $path = "images/{$share->type}/{$share->listing_id}/icon-".Str::random(16).'.webp';
                DataFiles::disk()->put($path, $webp, ['visibility' => 'public', 'ContentType' => 'image/webp']);
                $icon = ['url' => DataFiles::url($path), 'sha256' => hash('sha256', $webp), 'width' => 512, 'height' => 512, 'size' => strlen($webp)];
            }
        }

        $share->forceFill(['icon' => $icon])->save();
        ShareHost::propose($share, ! ShareHost::listed($share));
    }

    /**
     * Scales the image down to fit a square of $max pixels; a smaller one stays as it is.
     */
    private static function fit(GdImage $image, int $max): GdImage
    {
        $width = imagesx($image);
        $height = imagesy($image);
        $scale = min(1, $max / max($width, $height));

        if ($scale === 1) {
            return $image;
        }

        $fitted = imagecreatetruecolor(max(1, (int) round($width * $scale)), max(1, (int) round($height * $scale)));
        imagecopyresampled($fitted, $image, 0, 0, 0, 0, imagesx($fitted), imagesy($fitted), $width, $height);

        return $fitted;
    }

    /**
     * WebP of the image at the best quality that fits $limit bytes, or null when none does.
     */
    private static function webp(GdImage $image, int $limit): ?string
    {
        foreach ([82, 72, 60, 45] as $quality) {
            ob_start();
            imagewebp($image, null, $quality);
            $bytes = (string) ob_get_clean();

            if (strlen($bytes) <= $limit) {
                return $bytes;
            }
        }

        return null;
    }
}
