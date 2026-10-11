<script setup lang="ts">
import { Head, Link } from '@inertiajs/vue3';
import AppLayout from '@/layouts/AppLayout.vue';
import { accounts } from '@/routes';
import { show } from '@/routes/mods';

const props = defineProps<{
    listing: { id: string; name: string };
    // The repository ownership binds to, or null when the listing names none.
    repository: string | null;
    githubLogin: string | null;
}>();

const topic = `ksa-index-${(props.githubLogin ?? 'your-username').toLowerCase()}`;
const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const code = 'font-mono text-[#1b1b18] dark:text-[#EDEDEC]';
const link = 'underline underline-offset-4';
</script>

<template>
    <Head :title="`Edit ${listing.name}`" />

    <AppLayout>
        <div class="mx-auto max-w-3xl">
            <a
                :href="show(listing.id).url"
                :class="['text-sm hover:underline', muted]"
                >← {{ listing.name }}</a
            >
            <h1 class="text-2xl font-semibold tracking-tight">
                Only the mod's owner can edit this listing
            </h1>

            <div
                class="mt-6 flex flex-col gap-3 rounded-lg bg-white p-5 text-sm shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]"
            >
                <p v-if="githubLogin === null">
                    Editing a listing goes through GitHub, and your account has
                    no GitHub linked.
                    <Link :href="accounts()" :class="link"
                        >Link your GitHub account</Link
                    >, then come back to this page.
                </p>
                <template v-else-if="repository">
                    <p>
                        The KSA content index counts whoever controls
                        <a
                            :href="`https://github.com/${repository}`"
                            target="_blank"
                            rel="noopener noreferrer"
                            :class="[code, link]"
                            >{{ repository }}</a
                        >
                        as the owner of {{ listing.name }}, and your GitHub
                        account does not.
                    </p>
                    <p :class="muted">
                        Your account counts when it owns that repository, is an
                        admin of it, or the repository has the topic
                        <code :class="code">{{ topic }}</code
                        >. If you maintain the mod, ask someone who can change
                        the repository's settings to add that topic, then reload
                        this page.
                    </p>
                </template>
                <p v-else>
                    {{ listing.name }} names no GitHub repository its ownership
                    can be checked against, so nobody can edit it here. A
                    steward of the
                    <a
                        href="https://github.com/KSAModding/content-index/issues"
                        target="_blank"
                        rel="noopener noreferrer"
                        :class="link"
                        >content index</a
                    >
                    can change it.
                </p>
            </div>
        </div>
    </AppLayout>
</template>
