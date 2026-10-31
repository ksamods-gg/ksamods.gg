<x-mail::message>
Hi {{ $name }},

Confirm that ksamods.gg may send the digest of new releases you follow to this address.

<x-mail::button :url="$url">
Confirm my email
</x-mail::button>

The link works for 7 days. If you did not ask for this, ignore this email and nothing will be sent.
</x-mail::message>
