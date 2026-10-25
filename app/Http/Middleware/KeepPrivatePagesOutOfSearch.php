<?php

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\Request;
use Symfony\Component\HttpFoundation\Response;

/**
 * Tells search engines not to index pages that are only for one person: anything behind a
 * sign-in (admin, settings, uploads, notifications), the sign-in pages themselves, and the
 * device sign-in that clients such as Borea open. New routes in those groups are covered too.
 */
class KeepPrivatePagesOutOfSearch
{
    public function handle(Request $request, Closure $next): Response
    {
        $response = $next($request);
        $middleware = $request->route()?->gatherMiddleware() ?? [];

        if (array_intersect($middleware, ['auth', 'guest']) !== [] || $request->is('oauth/*', 'share/files/*')) {
            $response->headers->set('X-Robots-Tag', 'noindex, nofollow');
        }

        return $response;
    }
}
