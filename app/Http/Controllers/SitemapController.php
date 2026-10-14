<?php

namespace App\Http\Controllers;

use App\Support\ContentIndex;
use Illuminate\Http\Response;

/**
 * Every public page, so search engines find each mod without crawling the client-side mod list.
 */
class SitemapController extends Controller
{
    public function __invoke(): Response
    {
        $urls = collect([route('home'), route('mods.index'), route('privacy'), route('terms')])
            ->map(fn (string $url) => ['loc' => $url, 'lastmod' => null])
            ->concat(collect(ContentIndex::snapshot()['listings'])->map(fn (array $listing) => [
                'loc' => route('mods.show', $listing['id']),
                'lastmod' => $listing['updated_at'],
            ]));

        $xml = '<?xml version="1.0" encoding="UTF-8"?>'."\n"
            .'<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">'."\n"
            .$urls->map(fn (array $url) => '<url><loc>'.e($url['loc']).'</loc>'
                .($url['lastmod'] ? '<lastmod>'.e($url['lastmod']).'</lastmod>' : '')
                .'</url>')->implode("\n")
            ."\n</urlset>\n";

        return response($xml, 200, ['Content-Type' => 'application/xml']);
    }
}
