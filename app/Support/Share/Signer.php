<?php

namespace App\Support\Share;

use RuntimeException;

/**
 * Signs data files with the host's Ed25519 key (spec/share-host.md, rule 22): a detached signature
 * over the file's exact bytes, as padded standard base64. hosts.toml names the public key as base64
 * of its SubjectPublicKeyInfo, which for Ed25519 is a fixed 12-byte prefix before the 32-byte key.
 */
class Signer
{
    private const SPKI_PREFIX = "\x30\x2a\x30\x05\x06\x03\x2b\x65\x70\x03\x21\x00";

    /**
     * A new secret key, as SHARE_SIGNING_KEY takes it.
     */
    public static function generate(): string
    {
        return base64_encode(sodium_crypto_sign_secretkey(sodium_crypto_sign_keypair()));
    }

    public static function sign(string $bytes): string
    {
        return base64_encode(sodium_crypto_sign_detached($bytes, self::secretKey()));
    }

    /**
     * The public key as hosts.toml's `key` takes it.
     */
    public static function publicKey(): string
    {
        return base64_encode(self::SPKI_PREFIX.sodium_crypto_sign_publickey_from_secretkey(self::secretKey()));
    }

    /**
     * Whether `signature` is valid for `bytes` under a key in hosts.toml form, as the index checks it.
     */
    public static function verify(string $bytes, string $signature, string $publicKey): bool
    {
        $raw = base64_decode($publicKey, true);
        $signature = base64_decode(trim($signature), true);

        $point = $raw === false ? '' : substr($raw, 12);

        return $raw !== false && $signature !== false && strlen($raw) === 44 && str_starts_with($raw, self::SPKI_PREFIX)
            && strlen($signature) === SODIUM_CRYPTO_SIGN_BYTES && $point !== ''
            && sodium_crypto_sign_verify_detached($signature, $bytes, $point);
    }

    /**
     * @return non-empty-string
     */
    private static function secretKey(): string
    {
        $key = base64_decode((string) config('share.signing_key'), true);

        if ($key === false || $key === '' || strlen($key) !== SODIUM_CRYPTO_SIGN_SECRETKEYBYTES) {
            throw new RuntimeException('SHARE_SIGNING_KEY is not set to a key from php artisan share:key.');
        }

        return $key;
    }
}
