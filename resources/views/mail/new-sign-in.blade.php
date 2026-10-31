<x-mail::message>
# New sign-in

Hi {{ $name }}, your ksamods.gg account was just signed in to from a browser it had not seen before.

<x-mail::panel>
**{{ $device }}**@if ($ip) · {{ $ip }}@endif<br>
{{ $time }}
</x-mail::panel>

If this was you, there is nothing to do. If not, sign out of that session and check your GitHub and Discord accounts, which ksamods.gg signs in with.

<x-mail::button :url="$sessions">
See your sessions
</x-mail::button>
</x-mail::message>
