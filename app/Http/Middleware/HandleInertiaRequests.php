<?php

namespace App\Http\Middleware;

use App\Models\Report;
use Illuminate\Http\Request;
use Inertia\Middleware;

class HandleInertiaRequests extends Middleware
{
    /**
     * The root template that's loaded on the first page visit.
     *
     * @see https://inertiajs.com/server-side-setup#root-template
     *
     * @var string
     */
    protected $rootView = 'app';

    /**
     * Determines the current asset version.
     *
     * @see https://inertiajs.com/asset-versioning
     */
    public function version(Request $request): ?string
    {
        return parent::version($request);
    }

    /**
     * Define the props that are shared by default.
     *
     * @see https://inertiajs.com/shared-data
     *
     * @return array<string, mixed>
     */
    public function share(Request $request): array
    {
        return [
            ...parent::share($request),
            'name' => config('app.name'),
            // Link previews need absolute URLs.
            'appUrl' => rtrim((string) config('app.url'), '/'),
            // The browser's, so a server-side render decides platform-specific UI (such as Borea's
            // buttons) the same way the browser does; Node's own navigator says "Node.js".
            'userAgent' => (string) $request->userAgent(),
            'auth' => [
                'user' => $request->user(),
            ],
            // The header's bell; Reverb keeps it current between page loads.
            'unreadNotifications' => fn () => $request->user()?->unreadNotifications()->count() ?? 0,
            // The header's Admin link, for moderators and admins.
            'openReports' => fn () => $request->user()?->canModerate() ? Report::open()->count() : null,
        ];
    }
}
