<?php

namespace App\Http\Controllers;

use App\Models\Share;
use App\Support\ContentIndex;
use Illuminate\Http\Response;

/**
 * Every public page, so search engines find each mod, vehicle and save without crawling the lists.
 */
class SitemapController extends Controller
{
    public function __invoke(): Response
    {
        $pages = [
            route('home'), route('mods.index'), route('shares.index', 'vehicles'), route('shares.index', 'saves'),
            route('policy'), route('privacy'), route('terms'),
        ];

        $urls = collect($pages)
            ->map(fn (string $url) => ['loc' => $url, 'lastmod' => null])
            ->concat(collect(ContentIndex::snapshot()['listings'])->map(fn (array $listing) => [
                'loc' => route('mods.show', $listing['id']),
                'lastmod' => $listing['updated_at'],
            ]))
            // Vehicles and saves with a release that is still up, as their pages list them.
            ->concat(Share::whereHas('releases', fn ($query) => $query->whereNull('taken_down_at'))
                ->get(['type', 'listing_id', 'updated_at'])
                ->map(fn (Share $share) => ['loc' => $share->url(), 'lastmod' => $share->updated_at->toAtomString()]));

        $xml = '<?xml version="1.0" encoding="UTF-8"?>'."\n"
            .'<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">'."\n"
            .$urls->map(fn (array $url) => '<url><loc>'.e($url['loc']).'</loc>'
                .($url['lastmod'] ? '<lastmod>'.e($url['lastmod']).'</lastmod>' : '')
                .'</url>')->implode("\n")
            ."\n</urlset>\n";

        return response($xml, 200, ['Content-Type' => 'application/xml']);
    }
}
