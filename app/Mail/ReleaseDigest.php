<?php

namespace App\Mail;

use App\Models\User;
use Illuminate\Mail\Mailable;
use Illuminate\Mail\Mailables\Content;
use Illuminate\Mail\Mailables\Envelope;
use Illuminate\Mail\Mailables\Headers;
use Illuminate\Support\Facades\URL;

/**
 * New releases of what the player follows, since the last digest.
 */
class ReleaseDigest extends Mailable
{
    /**
     * @param  list<array{name: string, version: string, url: string}>  $releases
     */
    public function __construct(public User $user, public array $releases) {}

    public function envelope(): Envelope
    {
        $count = count($this->releases);

        return new Envelope(subject: $count === 1
            ? "{$this->releases[0]['name']} {$this->releases[0]['version']} is out"
            : "{$count} new releases on ksamods.gg");
    }

    /**
     * Mail clients show a one-click unsubscribe for this (RFC 8058).
     */
    public function headers(): Headers
    {
        return new Headers(text: [
            'List-Unsubscribe' => '<'.$this->unsubscribeUrl().'>',
            'List-Unsubscribe-Post' => 'List-Unsubscribe=One-Click',
        ]);
    }

    public function content(): Content
    {
        return new Content(markdown: 'mail.release-digest', with: [
            'name' => $this->user->name,
            'releases' => $this->releases,
            'frequency' => $this->user->digest_frequency,
            'settings' => route('settings.notifications'),
            'unsubscribe' => $this->unsubscribeUrl(),
        ]);
    }

    private function unsubscribeUrl(): string
    {
        return URL::signedRoute('digest.unsubscribe', ['user' => $this->user->id]);
    }
}
