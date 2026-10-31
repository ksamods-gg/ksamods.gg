<script setup lang="ts">
import { Head, Link, usePage } from '@inertiajs/vue3';
import { computed, ref } from 'vue';
import SettingsLayout from '@/layouts/SettingsLayout.vue';
import { cn } from '@/lib/utils';
import { destroy as deleteAccount } from '@/routes/account';
import { unlink } from '@/routes/accounts';
import { link as discordLink } from '@/routes/discord';
import { link as githubLink } from '@/routes/github';

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
const heading =
    'mb-4 border-b border-[#19140014] pb-2 text-xl font-semibold tracking-tight dark:border-[#3E3E3A]/60';
const danger =
    'border-[#f53003]/50 text-[#f53003] hover:border-[#f53003] dark:border-[#FF4433]/50 dark:text-[#FF4433] dark:hover:border-[#FF4433]';
const button =
    'shrink-0 rounded-md border border-[#19140035] px-3 py-1.5 text-sm hover:border-[#1915014a] disabled:cursor-not-allowed disabled:opacity-50 dark:border-[#3E3E3A] dark:hover:border-[#62605b]';
</script>

<template>
    <Head title="Account" />

    <SettingsLayout>
        <section>
            <h1 :class="heading">Linked accounts</h1>
            <p :class="['mb-4 text-sm', muted]">
                Sign in to ksamods.gg with any account linked here.
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
                    class="flex flex-wrap items-center justify-between gap-3 px-4 py-3.5"
                >
                    <div class="min-w-0">
                        <p class="text-sm font-medium">
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
        </section>

        <section>
            <h2 :class="heading">Danger zone</h2>
            <div
                class="flex flex-wrap items-center justify-between gap-4 rounded-lg border border-[#f53003]/50 px-4 py-3.5 text-sm dark:border-[#FF4433]/50"
            >
                <div class="min-w-0 flex-1 basis-72">
                    <p class="font-medium">Delete this account</p>
                    <p :class="['text-xs', muted]">
                        Deletes your ksamods.gg account with your reviews,
                        comments, reports, follows and notifications. Replies
                        other people wrote under your comments go too. Listings
                        you submitted stay in the content index, and pull
                        requests stay on GitHub.
                    </p>
                </div>
                <button
                    v-if="!confirmingDelete"
                    type="button"
                    :class="cn(button, danger)"
                    @click="confirmingDelete = true"
                >
                    Delete account…
                </button>
                <div v-else class="flex w-full flex-wrap items-center gap-3">
                    <p class="w-full font-medium">
                        This cannot be undone. Delete your account?
                    </p>
                    <Link
                        :href="deleteAccount()"
                        as="button"
                        class="rounded-md bg-[#f53003] px-3 py-1.5 text-sm font-medium text-white hover:bg-[#d42a02] dark:bg-[#FF4433] dark:text-[#0a0a0a]"
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
        </section>
    </SettingsLayout>
</template>
