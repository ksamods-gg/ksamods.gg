<script setup lang="ts">
import { Head, usePage } from '@inertiajs/vue3';
import { computed } from 'vue';

// The page title plus what search engines and link previews (Discord, Reddit, chat apps) show.
const props = defineProps<{
    title: string;
    description: string;
    // An absolute image URL; the site icon when there is none.
    image?: string | null;
}>();

const page = usePage();
const url = computed(() => page.props.appUrl + page.url.split('?')[0]);
const image = computed(
    () => props.image ?? `${page.props.appUrl}/apple-touch-icon.png`,
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
    </Head>
</template>
