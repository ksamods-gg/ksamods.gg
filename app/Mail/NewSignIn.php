<?php

namespace App\Mail;

use App\Models\User;
use Illuminate\Mail\Mailable;
use Illuminate\Mail\Mailables\Content;
use Illuminate\Mail\Mailables\Envelope;

/**
 * Someone signed in to the player's account from a browser it had not seen.
 */
class NewSignIn extends Mailable
{
    public function __construct(public User $user, public string $device, public ?string $ip) {}

    public function envelope(): Envelope
    {
        return new Envelope(subject: 'New sign-in to your ksamods.gg account');
    }

    public function content(): Content
    {
        return new Content(markdown: 'mail.new-sign-in', with: [
            'name' => $this->user->name,
            'device' => $this->device,
            'ip' => $this->ip,
            'time' => now()->utc()->format('j F Y, H:i').' UTC',
            'sessions' => route('settings.sessions'),
        ]);
    }
}
