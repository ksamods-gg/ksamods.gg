<?php

namespace App\Models;

// use Illuminate\Contracts\Auth\MustVerifyEmail;
use App\Enums\Role;
use App\Support\Share\DataFiles;
use Database\Factories\UserFactory;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Attributes\Hidden;
use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Illuminate\Foundation\Auth\User as Authenticatable;
use Illuminate\Notifications\Notifiable;
use Illuminate\Support\Carbon;
use Laravel\Passport\Contracts\OAuthenticatable;
use Laravel\Passport\HasApiTokens;

/**
 * @property int $id
 * @property string|null $github_id
 * @property string|null $github_login
 * @property string|null $github_token
 * @property string|null $github_scopes
 * @property string|null $discord_id
 * @property string|null $discord_username
 * @property string $name
 * @property string|null $email
 * @property string|null $avatar
 * @property Role|null $role
 * @property string|null $share_account
 * @property Carbon|null $share_consented_at
 * @property string|null $digest_email
 * @property Carbon|null $digest_email_verified_at
 * @property 'off'|'daily'|'weekly'|'monthly' $digest_frequency
 * @property Carbon|null $digest_sent_at
 * @property string|null $username
 * @property bool $profile_enabled
 * @property string|null $bio
 * @property list<string>|null $profile_links
 * @property bool $profile_show_accounts
 * @property array<string, bool>|null $profile_sections
 * @property list<string>|null $mod_ids
 * @property Carbon|null $email_verified_at
 * @property string|null $password
 * @property string|null $remember_token
 * @property Carbon|null $created_at
 * @property Carbon|null $updated_at
 */
#[Fillable(['github_id', 'github_login', 'github_token', 'github_scopes', 'discord_id', 'discord_username', 'name', 'email', 'avatar', 'password'])]
#[Hidden(['password', 'remember_token', 'github_token', 'share_account', 'mod_ids'])]
class User extends Authenticatable implements OAuthenticatable
{
    /** @use HasFactory<UserFactory> */
    use HasApiTokens, HasFactory, Notifiable;

    /**
     * An owner record names the GitHub account its owner signed in with, so it follows a link, an
     * unlink and an account deletion (an erasure, rule 32).
     */
    protected static function booted(): void
    {
        static::updated(function (User $user) {
            if ($user->wasChanged('github_id')) {
                $user->shares()->get()->each(fn (Share $share) => DataFiles::writeOwnerRecord($share));
            }
        });

        static::deleted(function (User $user) {
            if ($user->share_account !== null) {
                Share::where('account', $user->share_account)->get()->each(fn (Share $share) => DataFiles::writeOwnerRecord($share));
            }
        });
    }

    /**
     * The vehicles and saves this user shares on this host.
     *
     * @return HasMany<Share, $this>
     */
    public function shares(): HasMany
    {
        return $this->hasMany(Share::class);
    }

    /**
     * What a public profile can show; each is on until the player turns it off.
     */
    public const PROFILE_SECTIONS = ['vehicles', 'saves', 'mods', 'reviews', 'comments'];

    /**
     * The public profile's address, while it is turned on.
     */
    public function profileUrl(): ?string
    {
        return $this->profile_enabled && $this->username !== null ? route('profiles.show', $this->username) : null;
    }

    public function showsOnProfile(string $section): bool
    {
        return $this->profile_sections[$section] ?? true;
    }

    /**
     * @return HasMany<Follow, $this>
     */
    public function follows(): HasMany
    {
        return $this->hasMany(Follow::class);
    }

    /**
     * @param  'mod'|'vehicle'|'save'  $kind
     */
    public function isFollowing(string $kind, string $listingId): bool
    {
        return $this->follows()->where('kind', $kind)->where('listing_id', $listingId)->exists();
    }

    /**
     * @return HasMany<Review, $this>
     */
    public function reviews(): HasMany
    {
        return $this->hasMany(Review::class);
    }

    /**
     * @return HasMany<Comment, $this>
     */
    public function comments(): HasMany
    {
        return $this->hasMany(Comment::class);
    }

    /**
     * Whether the user may delete a review or comment: its author, or a moderator.
     */
    public function canDelete(Review|Comment $post): bool
    {
        return $this->canModerate() || $post->user_id === $this->id;
    }

    /**
     * Moderators and admins: the admin panel, deleting any post, and handling reports.
     */
    public function canModerate(): bool
    {
        return $this->role !== null;
    }

    /**
     * Admins alone change other users' roles.
     */
    public function isAdmin(): bool
    {
        return $this->role === Role::Admin;
    }

    /**
     * Whether the stored token may fork the index and open a pull request from this account.
     */
    public function canOpenPullRequests(): bool
    {
        $scopes = explode(',', (string) $this->github_scopes);

        return $this->github_token !== null && (in_array('public_repo', $scopes) || in_array('repo', $scopes));
    }

    /**
     * Get the attributes that should be cast.
     *
     * @return array<string, string>
     */
    protected function casts(): array
    {
        return [
            'email_verified_at' => 'datetime',
            'password' => 'hashed',
            'github_token' => 'encrypted',
            'role' => Role::class,
            'share_consented_at' => 'datetime',
            'digest_email_verified_at' => 'datetime',
            'digest_sent_at' => 'datetime',
            'profile_enabled' => 'boolean',
            'profile_links' => 'array',
            'profile_show_accounts' => 'boolean',
            'profile_sections' => 'array',
            'mod_ids' => 'array',
        ];
    }
}
