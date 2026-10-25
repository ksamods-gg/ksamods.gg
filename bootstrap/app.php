<?php

use App\Http\Middleware\HandleInertiaRequests;
use App\Http\Middleware\KeepPrivatePagesOutOfSearch;
use App\Support\Share\ShareError;
use Illuminate\Auth\AuthenticationException;
use Illuminate\Foundation\Application;
use Illuminate\Foundation\Configuration\Exceptions;
use Illuminate\Foundation\Configuration\Middleware;
use Illuminate\Http\Exceptions\ThrottleRequestsException;
use Illuminate\Http\Middleware\AddLinkHeadersForPreloadedAssets;
use Illuminate\Http\Request;
use Illuminate\Validation\ValidationException;

return Application::configure(basePath: dirname(__DIR__))
    ->withRouting(
        web: __DIR__.'/../routes/web.php',
        api: __DIR__.'/../routes/api.php',
        apiPrefix: 'api/share/v1',
        commands: __DIR__.'/../routes/console.php',
        channels: __DIR__.'/../routes/channels.php',
        health: '/up',
    )
    ->withMiddleware(function (Middleware $middleware): void {
        // Coolify's proxy terminates TLS; trust it so URLs are generated as https.
        $middleware->trustProxies(at: '*');

        $middleware->web(append: [
            HandleInertiaRequests::class,
            AddLinkHeadersForPreloadedAssets::class,
            KeepPrivatePagesOutOfSearch::class,
        ]);
    })
    ->withExceptions(function (Exceptions $exceptions): void {
        $exceptions->shouldRenderJsonWhen(
            fn (Request $request) => $request->is('api/*') || $request->expectsJson(),
        );

        // The share host's API answers every error as { "error", "message" } (rule 16).
        $exceptions->render(fn (ShareError $error) => response()->json($error->toArray(), $error->status));
        $exceptions->render(fn (AuthenticationException $error, Request $request) => $request->is('api/share/*')
            ? response()->json(['error' => 'unauthorized', 'message' => 'Sign in first: the token is missing or has expired.'], 401)
            : null);
        $exceptions->render(fn (ValidationException $error, Request $request) => $request->is('api/share/*')
            ? response()->json(['error' => 'invalid_request', 'message' => $error->getMessage(), 'errors' => $error->errors()], 422)
            : null);
        $exceptions->render(fn (ThrottleRequestsException $error, Request $request) => $request->is('api/share/*')
            ? response()->json([
                'error' => 'rate_limited',
                'message' => 'Too many requests. Try again later.',
                'retry_after' => (int) ($error->getHeaders()['Retry-After'] ?? 60),
            ], 429, $error->getHeaders())
            : null);
    })->create();
