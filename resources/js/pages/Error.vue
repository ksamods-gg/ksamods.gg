<script setup lang="ts">
import { Head, Link, router, usePage } from '@inertiajs/vue3';
import { computed, ref } from 'vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { home } from '@/routes';
import { index as mods } from '@/routes/mods';

/**
 * The site's error pages. Server errors (500, 503) arrive without the shared data the layout
 * needs, since that data may be what failed, so they render in a plain shell of their own.
 */
const props = defineProps<{ status: 403 | 404 | 419 | 429 | 500 | 503 }>();

const page = usePage();
const withLayout = computed(() => page.props.auth !== undefined);

const messages = {
    403: {
        title: 'You can’t open this page',
        body: 'This page is for someone else, such as the owner of a mod or a moderator.',
    },
    404: {
        title: 'This page doesn’t exist',
        body: 'The link may be old, or the mod may have left the index. Try searching for it.',
    },
    419: {
        title: 'This page expired',
        body: 'It was open for a while, so the form on it timed out. Go back, reload the page and try again.',
    },
    429: {
        title: 'Slow down a little',
        body: 'That was a lot of requests in a short time. Wait a minute, then try again.',
    },
    500: {
        title: 'Something went wrong on our side',
        body: 'The error was logged, and it isn’t anything you did. Try again in a moment.',
    },
    503: {
        title: 'Back in a moment',
        body: 'ksamods.gg is being updated. It should be back within a few minutes.',
    },
} as const;

const message = computed(() => messages[props.status]);

const search = ref('');

function reload() {
    window.location.reload();
}

function find() {
    router.get(
        mods().url,
        search.value.trim() ? { q: search.value.trim() } : {},
    );
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const button =
    'rounded-md bg-[#1b1b18] px-4 py-2 text-sm font-medium text-white hover:bg-black dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white';
const secondary =
    'rounded-md border border-[#19140035] px-4 py-2 text-sm font-medium hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]';
</script>

<template>
    <Head :title="message.title" />

    <component
        :is="withLayout ? AppLayout : 'div'"
        :class="
            withLayout
                ? undefined
                : 'flex min-h-screen flex-col bg-[#FDFDFC] text-[#1b1b18] dark:bg-[#0a0a0a] dark:text-[#EDEDEC]'
        "
    >
        <!-- The plain shell's header: the site's name, which needs no shared data. -->
        <header
            v-if="!withLayout"
            class="border-b border-[#19140014] px-4 py-3 sm:px-6 dark:border-[#3E3E3A]/60"
        >
            <a
                href="/"
                class="flex items-center gap-2 font-semibold tracking-tight"
            >
                <img
                    src="/favicon.svg"
                    alt=""
                    width="24"
                    height="24"
                    class="size-6"
                />
                ksamods.gg
            </a>
        </header>

        <section
            :class="[
                'mx-auto w-full max-w-xl py-16 text-center sm:py-24',
                withLayout ? '' : 'flex-1 px-4',
            ]"
        >
            <p :class="['font-mono text-sm tabular-nums', muted]">
                Error {{ status }}
            </p>
            <h1 class="mt-2 text-3xl font-semibold tracking-tight text-balance">
                {{ message.title }}
            </h1>
            <p :class="['mx-auto mt-3 max-w-md text-pretty', muted]">
                {{ message.body }}
            </p>

            <form
                v-if="status === 404"
                role="search"
                class="mx-auto mt-8 flex max-w-sm gap-2"
                @submit.prevent="find"
            >
                <label class="min-w-0 flex-1">
                    <span class="sr-only">Search mods</span>
                    <input
                        v-model="search"
                        type="search"
                        placeholder="Search mods"
                        class="w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC]"
                    />
                </label>
                <button type="submit" :class="button">Search</button>
            </form>

            <div class="mt-8 flex flex-wrap justify-center gap-3">
                <!-- Plain links on server errors: a full page load starts over cleanly. -->
                <template v-if="withLayout">
                    <Link
                        :href="home()"
                        :class="status === 404 ? secondary : button"
                        >Go to the home page</Link
                    >
                    <Link
                        v-if="status !== 404"
                        :href="mods()"
                        :class="secondary"
                        >Browse mods</Link
                    >
                </template>
                <template v-else>
                    <a href="/" :class="button">Go to the home page</a>
                    <button type="button" :class="secondary" @click="reload">
                        Try again
                    </button>
                </template>
            </div>
        </section>
    </component>
</template>
