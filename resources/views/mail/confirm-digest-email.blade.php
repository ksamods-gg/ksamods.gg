<x-mail::message>
# {{ __('Confirm your email') }}

{{ __('Hi :name, confirm that ksamods.gg may send the digest of new releases you follow to this address.', ['name' => $name]) }}

<x-mail::button :url="$url">
{{ __('Confirm my email') }}
</x-mail::button>

<x-slot:subcopy>
{{ __('The link works for 7 days. If you did not ask for this, ignore this email and nothing will be sent.') }}
</x-slot:subcopy>
</x-mail::message>
