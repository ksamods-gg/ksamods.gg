<?php

namespace App\Http\Controllers;

use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Notifications\DatabaseNotification;
use Inertia\Inertia;
use Inertia\Response;

class NotificationController extends Controller
{
    // ponytail: the newest only; page through them once people keep this many.
    private const SHOWN = 50;

    public function index(Request $request): Response
    {
        return Inertia::render('Notifications/Index', [
            'notifications' => $request->user()->notifications()->latest()->limit(self::SHOWN)->get()
                ->map(fn (DatabaseNotification $notification) => [
                    'id' => $notification->id,
                    'data' => $notification->data,
                    'read' => $notification->read_at !== null,
                    'created_at' => $notification->created_at?->toIso8601String(),
                ]),
        ]);
    }

    /**
     * Marks the notification read and goes to the comment it is about.
     */
    public function open(Request $request, string $id): RedirectResponse
    {
        $notification = $request->user()->notifications()->findOrFail($id);
        $notification->markAsRead();

        $data = $notification->data;

        return redirect()->to(
            route('mods.show', ['id' => $data['mod']['id'], 'tab' => $data['tab']]).'#comment-'.$data['comment_id']
        );
    }

    public function readAll(Request $request): RedirectResponse
    {
        $request->user()->unreadNotifications()->update(['read_at' => now()]);

        return back();
    }
}
