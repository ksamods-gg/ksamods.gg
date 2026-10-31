{!! '<?xml version="1.0" encoding="UTF-8"?>' !!}
<rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom">
    <channel>
        <title>{{ $title }}</title>
        <link>{{ $alternate }}</link>
        <description>{{ $title }}, from ksamods.gg</description>
        <atom:link rel="self" type="application/rss+xml" href="{{ $self }}"/>
        <lastBuildDate>{{ $updated->toRfc2822String() }}</lastBuildDate>
@foreach ($items as $item)
        <item>
            <title>{{ $item['title'] }}</title>
            <link>{{ $item['url'] }}</link>
            <guid isPermaLink="false">{{ $item['id'] }}</guid>
            <pubDate>{{ $item['date']->toRfc2822String() }}</pubDate>
            <description>{{ $item['summary'] }}</description>
        </item>
@endforeach
    </channel>
</rss>
