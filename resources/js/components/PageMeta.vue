<script setup lang="ts">
import { Head, usePage } from '@inertiajs/vue3';
import { computed, h } from 'vue';

// The page title plus what search engines and link previews (Discord, Reddit, chat apps) show.
const props = defineProps<{
    title: string;
    description: string;
    // An absolute image URL; the site icon when there is none.
    image?: string | null;
    // schema.org data for search results, such as a mod as a SoftwareApplication.
    jsonLd?: Record<string, unknown> | null;
}>();

const page = usePage();

// Query strings are left out, so filtered and sorted views count as the one page; only a page
// number past the first stays, because those pages list other things.
const url = computed(() => {
    const [path, query] = page.url.split('?');
    const number = new URLSearchParams(query ?? '').get('page');

    return (
        page.props.appUrl +
        path +
        (number && number !== '1' ? `?page=${encodeURIComponent(number)}` : '')
    );
});
const image = computed(
    () => props.image ?? `${page.props.appUrl}/apple-touch-icon.png`,
);

// A script tag rendered from a node, since templates cannot hold one. Inertia's head renders its
// text as is, so < is escaped to keep listing text from closing the tag.
const jsonLd = computed(() =>
    props.jsonLd
        ? h(
              'script',
              { type: 'application/ld+json', 'head-key': 'json-ld' },
              JSON.stringify({
                  '@context': 'https://schema.org',
                  ...props.jsonLd,
              }).replace(/</g, '\\u003c'),
          )
        : null,
);
</script>

<template>
    <Head :title="title">
        <meta
            head-key="description"
            name="description"
            :content="description"
        />
        <link head-key="canonical" rel="canonical" :href="url" />
        <meta head-key="og:type" property="og:type" content="website" />
        <meta
            head-key="og:site_name"
            property="og:site_name"
            :content="page.props.name"
        />
        <meta head-key="og:title" property="og:title" :content="title" />
        <meta
            head-key="og:description"
            property="og:description"
            :content="description"
        />
        <meta head-key="og:url" property="og:url" :content="url" />
        <meta head-key="og:image" property="og:image" :content="image" />
        <meta head-key="twitter:card" name="twitter:card" content="summary" />
        <component :is="jsonLd" v-if="jsonLd" />
    </Head>
</template>
