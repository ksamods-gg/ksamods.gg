<?php

namespace App\Http\Middleware;

use App\Support\Translations;
use Closure;
use Illuminate\Http\Request;
use Illuminate\Support\Carbon;
use Symfony\Component\HttpFoundation\Response;

/**
 * The language of the page: the player's choice, from their account or a cookie, else the
 * browser's preferred one among the active languages, else English.
 */
class SetLocale
{
    public function handle(Request $request, Closure $next): Response
    {
        $active = array_column(Translations::languages(), 'code');
        $chosen = $request->user()->locale ?? $request->cookie('locale');

        $locale = match (true) {
            $chosen === 'en' => 'en',
            is_string($chosen) && in_array($chosen, $active, true) => $chosen,
            default => $request->getPreferredLanguage(['en', ...$active]) ?? 'en',
        };

        app()->setLocale($locale);
        // "3 days ago" and other dates in the same language.
        Carbon::setLocale($locale);

        return $next($request);
    }
}
