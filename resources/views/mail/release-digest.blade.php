<x-mail::message>
Hi {{ $name }},

New releases of the mods, vehicles and saves you follow:

@foreach ($releases as $release)
- [{{ $release['name'] }} {{ $release['version'] }}]({{ $release['url'] }})
@endforeach

You get this {{ $frequency }}. [Change how often]({{ $settings }}) or [unsubscribe]({{ $unsubscribe }}).
</x-mail::message>
