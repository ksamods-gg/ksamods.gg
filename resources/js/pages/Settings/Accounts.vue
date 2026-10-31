<script setup lang="ts">
import { Head, Link, usePage } from '@inertiajs/vue3';
import { computed, ref } from 'vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { destroy as deleteAccount } from '@/routes/account';
import { unlink } from '@/routes/accounts';
import { link as discordLink } from '@/routes/discord';
import { link as githubLink } from '@/routes/github';
import { notifications as notificationSettings } from '@/routes/settings';

const props = defineProps<{
    github: { login: string | null; canOpenPullRequests: boolean } | null;
    discord: { username: string | null } | null;
}>();

const page = usePage();

const providers = computed(() => [
    {
        id: 'github',
        name: 'GitHub',
        linkedAs: props.github ? `@${props.github.login}` : null,
        note: props.github
            ? props.github.canOpenPullRequests
                ? 'Can open pull requests for your listings.'
                : 'Asked for pull request access when you first submit or edit a listing.'
            : 'Needed to submit and edit mod listings.',
        connect: githubLink().url,
    },
    {
        id: 'discord',
        name: 'Discord',
        linkedAs: props.discord ? props.discord.username : null,
        note: props.discord ? null : 'Another way to sign in.',
        connect: discordLink().url,
    },
]);

// Unlinking the only account would leave no way to sign in.
const linkedCount = computed(
    () => providers.value.filter((p) => p.linkedAs !== null).length,
);

// Deleting asks twice: the first click only shows what will go.
const confirmingDelete = ref(false);

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const button =
    'shrink-0 rounded-md border border-[#19140035] px-3 py-1.5 text-sm hover:border-[#1915014a] disabled:cursor-not-allowed disabled:opacity-50 dark:border-[#3E3E3A] dark:hover:border-[#62605b]';
</script>

<template>
    <Head title="Account" />

    <AppLayout>
        <div class="mx-auto max-w-2xl">
            <h1 class="text-2xl font-semibold tracking-tight">
                Linked accounts
            </h1>
            <p :class="['mt-1 mb-6 text-sm', muted]">
                Sign in to ksamods.gg with any account linked here.
                <Link
                    :href="notificationSettings()"
                    class="underline underline-offset-4"
                    >Notification settings</Link
                >
            </p>

            <p
                v-if="page.props.errors.link"
                role="alert"
                class="mb-4 rounded-md bg-[#fff2f2] p-3 text-sm text-[#f53003] dark:bg-[#1D0002] dark:text-[#FF4433]"
            >
                {{ page.props.errors.link }}
            </p>

            <ul
                class="divide-y divide-[#19140014] rounded-lg bg-white shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:divide-[#3E3E3A]/60 dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]"
            >
                <li
                    v-for="provider in providers"
                    :key="provider.id"
                    class="flex flex-wrap items-center justify-between gap-3 p-5"
                >
                    <div class="min-w-0">
                        <p class="font-medium">
                            {{ provider.name }}
                            <span
                                v-if="provider.linkedAs"
                                :class="['font-normal', muted]"
                                >· {{ provider.linkedAs }}</span
                            >
                        </p>
                        <p v-if="provider.note" :class="['text-xs', muted]">
                            {{ provider.note }}
                        </p>
                    </div>

                    <Link
                        v-if="provider.linkedAs"
                        :href="unlink(provider.id)"
                        as="button"
                        :disabled="linkedCount < 2"
                        :title="
                            linkedCount < 2
                                ? 'Link another account first, so you can still sign in.'
                                : undefined
                        "
                        :class="button"
                    >
                        Unlink
                    </Link>
                    <!-- A plain link: it leaves the site for the provider's sign-in. -->
                    <a v-else :href="provider.connect" :class="button">
                        Connect {{ provider.name }}
                    </a>
                </li>
            </ul>

            <h2 class="mt-10 font-semibold">Delete account</h2>
            <div
                class="mt-3 rounded-lg bg-white p-5 text-sm shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]"
            >
                <p>
                    Deletes your ksamods.gg account with your reviews, comments,
                    reports and notifications. Replies other people wrote under
                    your comments go too. Listings you submitted stay in the
                    content index, and pull requests stay on GitHub.
                </p>
                <button
                    v-if="!confirmingDelete"
                    type="button"
                    :class="[button, 'mt-4']"
                    @click="confirmingDelete = true"
                >
                    Delete account…
                </button>
                <div v-else class="mt-4 flex flex-wrap items-center gap-3">
                    <p class="w-full font-medium">
                        This cannot be undone. Delete your account?
                    </p>
                    <Link
                        :href="deleteAccount()"
                        as="button"
                        class="rounded-md bg-[#1b1b18] px-3 py-1.5 text-sm font-medium text-white hover:bg-black dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                    >
                        Yes, delete my account
                    </Link>
                    <button
                        type="button"
                        :class="button"
                        @click="confirmingDelete = false"
                    >
                        Cancel
                    </button>
                </div>
            </div>
        </div>
    </AppLayout>
</template>
