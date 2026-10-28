<?php

namespace App\Providers;

use App\Http\Controllers\Api\ShareDeviceController;
use App\Models\User;
use Carbon\CarbonImmutable;
use Illuminate\Cache\RateLimiting\Limit;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Date;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Event;
use Illuminate\Support\Facades\Gate;
use Illuminate\Support\Facades\RateLimiter;
use Illuminate\Support\ServiceProvider;
use Illuminate\Support\Str;
use Illuminate\Validation\Rules\Password;
use Inertia\ExceptionResponse;
use Inertia\Inertia;
use Laravel\Passport\Events\AccessTokenCreated;
use Laravel\Passport\Passport;
use SocialiteProviders\Discord\DiscordExtendSocialite;
use SocialiteProviders\Manager\SocialiteWasCalled;

class AppServiceProvider extends ServiceProvider
{
    /**
     * Register any application services.
     */
    public function register(): void
    {
        //
    }

    /**
     * Bootstrap any application services.
     */
    public function boot(): void
    {
        $this->configureDefaults();

        Event::listen(SocialiteWasCalled::class, [DiscordExtendSocialite::class, 'handle']);

        // The admin panel, and the part of it only admins use.
        Gate::define('moderate', fn (User $user) => $user->canModerate());
        Gate::define('administer', fn (User $user) => $user->isAdmin());

        // Writing reviews and comments.
        RateLimiter::for('social', fn (Request $request) => Limit::perMinute(10)->by($request->user()?->id ?: $request->ip()));

        $this->configureShareHost();
        $this->configureErrorPages();
    }

    /**
     * Errors as pages of the site rather than Laravel's own. The upload API and other JSON
     * requests keep their JSON errors, and with debug on a server error keeps Laravel's page.
     */
    protected function configureErrorPages(): void
    {
        Inertia::handleExceptionsUsing(function (ExceptionResponse $response) {
            $status = $response->statusCode();
            $request = $response->request;

            if (! in_array($status, [403, 404, 419, 429, 500, 503], true) || $request->is('api/*') || $request->expectsJson()) {
                return null;
            }

            if ($status >= 500 && config('app.debug')) {
                return null;
            }

            $response->render('Error', ['status' => $status]);

            // A server error may be the database itself, which the shared data reads, so those
            // pages render with nothing but their status. The request may have shared it already.
            if ($status >= 500) {
                Inertia::flushShared();

                return $response;
            }

            return $response->withSharedData();
        });
    }

    /**
     * The share host's device sign-in (RFC 8628 through Passport) and its limits.
     */
    protected function configureShareHost(): void
    {
        Passport::tokensExpireIn(now()->addDays(90));
        Passport::refreshTokensExpireIn(now()->addDays(180));

        // The page a player enters the code on, and the page that asks them to approve the client.
        Passport::deviceUserCodeView(fn (array $parameters) => Inertia::render('Device/Code', [
            'code' => $parameters['request']->old('user_code'),
            // authorization-approved or authorization-denied, after the player answered.
            'status' => $parameters['request']->session()->get('status'),
        ])->toResponse($parameters['request']));
        Passport::deviceAuthorizationView(fn (array $parameters) => Inertia::render('Device/Authorize', [
            'client' => Str::after($parameters['client']->name, ShareDeviceController::PREFIX),
            'userCode' => $parameters['request']->query('user_code'),
            'authToken' => $parameters['authToken'],
        ])->toResponse($parameters['request']));

        // The approval page tells the player that a listing goes into a public index that keeps its
        // history (RFC 0094, Guide), so approving a client is their consent, as on the upload page.
        Event::listen(AccessTokenCreated::class, fn (AccessTokenCreated $event) => User::whereKey($event->userId)
            ->whereNull('share_consented_at')
            ->update(['share_consented_at' => now()]));

        // Polling for the token is part of the flow, so sign-in allows a steady trickle per address.
        RateLimiter::for('share-sign-in', fn (Request $request) => Limit::perMinute(30)->by($request->ip()));
        // A limit of the host besides new_listings_per_day (rule 16, rate_limited).
        RateLimiter::for('share-uploads', fn (Request $request) => Limit::perHour(30)->by($request->user()?->id ?: $request->ip()));
    }

    /**
     * Configure default behaviors for production-ready applications.
     */
    protected function configureDefaults(): void
    {
        Date::use(CarbonImmutable::class);

        DB::prohibitDestructiveCommands(
            app()->isProduction(),
        );

        Password::defaults(fn (): ?Password => app()->isProduction()
            ? Password::min(12)
                ->mixedCase()
                ->letters()
                ->numbers()
                ->symbols()
                ->uncompromised()
            : null,
        );
    }
}
