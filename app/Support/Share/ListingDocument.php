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
        return Toml::encode([
            'spec_version' => 1,
            'id' => $share->listing_id,
            'type' => $share->type,
            'name' => $share->name,
            'authors' => $share->authors,
            'abstract' => $share->abstract,
            'description' => $share->description,
            'license' => $share->license,
            'tags' => $share->tags,
            'links' => ['homepage' => $share->url()],
            'releases' => [config('share.key') => $share->listing_id],
            'dependencies' => $share->dependencies,
        ]);
    }
}
