<?php

namespace App\Mail;

use App\Models\User;
use Illuminate\Mail\Mailable;
use Illuminate\Mail\Mailables\Content;
use Illuminate\Mail\Mailables\Envelope;
use Illuminate\Support\Facades\URL;

/**
 * Asks the player to confirm the address their digest goes to.
 */
class ConfirmDigestEmail extends Mailable
{
    public function __construct(public User $user) {}

    public function envelope(): Envelope
    {
        return new Envelope(subject: 'Confirm your email for ksamods.gg');
    }

    public function content(): Content
    {
        return new Content(markdown: 'mail.confirm-digest-email', with: [
            'name' => $this->user->name,
            'url' => URL::temporarySignedRoute('digest.confirm', now()->addDays(7), [
                'user' => $this->user->id,
                'email' => sha1((string) $this->user->digest_email),
            ]),
        ]);
    }
}
