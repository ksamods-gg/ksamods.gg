<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Laravel\Passport\ClientRepository;
use Laravel\Passport\Http\Controllers\AccessTokenController;
use Laravel\Passport\Http\Controllers\DeviceCodeController;
use Laravel\Passport\Passport;
use Symfony\Component\HttpFoundation\Response;

/**
 * The sign-in of the upload API (rule 15): RFC 8628's device authorization grant, served by
 * Passport. A host accepts any `client_id` as a public client, because a registry of clients per
 * host would undo a shared API, so each name a client gives, such as `borea`, gets its own public
 * Passport client the first time it is used, and the player sees that name when approving.
 */
class ShareDeviceController extends Controller
{
    public const PREFIX = 'device:';

    public function authorization(Request $request): Response
    {
        $this->asPassportClient($request);

        return app()->call([app(DeviceCodeController::class), '__invoke']);
    }

    public function token(Request $request): Response
    {
        if ($request->input('grant_type') === 'urn:ietf:params:oauth:grant-type:device_code') {
            $this->asPassportClient($request);
        }

        return app()->call([app(AccessTokenController::class), 'issueToken']);
    }

    /**
     * Replaces the client's name with the id of the Passport client made for that name.
     */
    private function asPassportClient(Request $request): void
    {
        $name = (string) $request->input('client_id');

        if (preg_match('/^[A-Za-z0-9._-]{1,64}$/', $name) !== 1) {
            abort(new JsonResponse([
                'error' => 'invalid_client',
                'error_description' => 'client_id must name the client in 1 to 64 letters, digits, ., _ or -, such as borea.',
            ], 400));
        }

        $client = Passport::client()->newQuery()->where('name', self::PREFIX.$name)->first()
            ?? app(ClientRepository::class)->createDeviceAuthorizationGrantClient(self::PREFIX.$name, confidential: false);

        $request->merge(['client_id' => $client->getKey()]);
    }
}
