<?php

namespace App\Models;

// use Illuminate\Contracts\Auth\MustVerifyEmail;
use Database\Factories\UserFactory;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Attributes\Hidden;
use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Foundation\Auth\User as Authenticatable;
use Illuminate\Notifications\Notifiable;
use Illuminate\Support\Carbon;

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
 * @property Carbon|null $email_verified_at
 * @property string|null $password
 * @property string|null $remember_token
 * @property Carbon|null $created_at
 * @property Carbon|null $updated_at
 */
#[Fillable(['github_id', 'github_login', 'github_token', 'github_scopes', 'discord_id', 'discord_username', 'name', 'email', 'avatar', 'password'])]
#[Hidden(['password', 'remember_token', 'github_token'])]
class User extends Authenticatable
{
    /** @use HasFactory<UserFactory> */
    use HasFactory, Notifiable;

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
        ];
    }
}
