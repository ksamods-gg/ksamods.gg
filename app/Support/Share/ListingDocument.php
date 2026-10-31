<?php

namespace App\Support\Share;

use App\Models\Share;
use App\Support\Toml;

/**
 * The authored document of a shared vehicle or save, vehicles/<id>.toml or saves/<id>.toml in the
 * authored repository (RFC 0085 with RFC 0094's [links] homepage and [releases] host key).
 */
class ListingDocument
{
    public static function path(Share $share): string
    {
        return Share::FOLDERS[$share->type]."/{$share->listing_id}.toml";
    }

    public static function toml(Share $share): string
    {
        $records = array_values($share->images()->get()->map->record()->all());

        return Toml::encode([
            'spec_version' => 1,
            'id' => $share->listing_id,
            'type' => $share->type,
            'name' => $share->name,
            'authors' => $share->authors,
            'abstract' => $share->abstract,
            'description' => self::description($share->description, $records),
            'license' => $share->license,
            'tags' => $share->tags,
            'links' => ['homepage' => $share->url()],
            'releases' => [config('share.key') => $share->listing_id],
            'dependencies' => $share->dependencies,
            // RFC 0058: the cover's square crop, and the screenshots the description shows.
            'images' => array_filter([
                'icon' => $share->icon,
                'description' => $records,
            ]),
        ]);
    }

    /**
     * The owner's description, with a section that shows the screenshots: a client shows only the
     * images the description references.
     *
     * @param  list<array{id: string}>  $screenshots
     */
    private static function description(?string $description, array $screenshots): ?string
    {
        if ($screenshots === []) {
            return $description;
        }

        $gallery = "## Screenshots\n\n".implode("\n\n", array_map(
            fn (array $image, int $index) => '![Screenshot '.($index + 1)."](ksa-image:{$image['id']})",
            $screenshots,
            array_keys($screenshots),
        ));

        return trim(trim((string) $description)."\n\n".$gallery);
    }
}
