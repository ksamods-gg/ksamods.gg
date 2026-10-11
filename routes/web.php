<?php

use App\Http\Controllers\AccountController;
use App\Http\Controllers\CommentController;
use App\Http\Controllers\DiscordAuthController;
use App\Http\Controllers\GitHubAuthController;
use App\Http\Controllers\ListingController;
use App\Http\Controllers\ModController;
use App\Http\Controllers\NotificationController;
use App\Http\Controllers\ReviewController;
use Illuminate\Support\Facades\Route;

Route::get('/', [ModController::class, 'home'])->name('home');
Route::get('/mods', [ModController::class, 'index'])->name('mods.index');
Route::get('/mods/{id}', [ModController::class, 'show'])->name('mods.show');

Route::middleware('guest')->group(function () {
    Route::inertia('/login', 'Auth/Login')->name('login');
    Route::get('/auth/github', [GitHubAuthController::class, 'redirect'])->name('github.login');
    Route::get('/auth/discord', [DiscordAuthController::class, 'redirect'])->name('discord.login');
});

// Both signing in and linking come back here, so these take guests and signed-in users alike.
Route::get('/auth/github/callback', [GitHubAuthController::class, 'callback']);
Route::get('/auth/discord/callback', [DiscordAuthController::class, 'callback']);

Route::middleware('auth')->group(function () {
    Route::get('/my-mods', [ModController::class, 'mine'])->name('mods.mine');
    Route::get('/settings/accounts', [AccountController::class, 'show'])->name('accounts');
    Route::delete('/settings/accounts/{provider}', [AccountController::class, 'unlink'])->name('accounts.unlink');
    Route::get('/auth/github/link', [GitHubAuthController::class, 'link'])->name('github.link');
    Route::get('/auth/discord/link', [DiscordAuthController::class, 'link'])->name('discord.link');
    Route::get('/auth/github/pull-requests', [GitHubAuthController::class, 'authorizePullRequests'])->name('github.pull-requests');
    Route::get('/listings/create', [ListingController::class, 'create'])->name('listings.create');
    Route::post('/listings', [ListingController::class, 'store'])->name('listings.store');
    Route::get('/mods/{id}/edit', [ListingController::class, 'edit'])->name('listings.edit');
    Route::put('/mods/{id}', [ListingController::class, 'update'])->name('listings.update');
    Route::post('/logout', [GitHubAuthController::class, 'logout'])->name('logout');
    Route::get('/notifications', [NotificationController::class, 'index'])->name('notifications');
    Route::post('/notifications/read', [NotificationController::class, 'readAll'])->name('notifications.read-all');
    Route::get('/notifications/{id}', [NotificationController::class, 'open'])->name('notifications.open');

    Route::middleware('throttle:social')->group(function () {
        Route::put('/mods/{id}/review', [ReviewController::class, 'save'])->name('reviews.save');
        Route::delete('/reviews/{review}', [ReviewController::class, 'destroy'])->name('reviews.destroy');
        Route::post('/mods/{id}/comments', [CommentController::class, 'store'])->name('comments.store');
        Route::patch('/comments/{comment}', [CommentController::class, 'update'])->name('comments.update');
        Route::delete('/comments/{comment}', [CommentController::class, 'destroy'])->name('comments.destroy');
    });
});
