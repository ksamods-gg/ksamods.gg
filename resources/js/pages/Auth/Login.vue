<script setup lang="ts">
import { Head } from '@inertiajs/vue3';
import AppLayout from '@/layouts/AppLayout.vue';
import { login as discordLogin } from '@/routes/discord';
import { login as githubLogin } from '@/routes/github';

const button =
    'block rounded-md border border-[#19140035] px-4 py-2.5 text-center text-sm font-medium hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]';
const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <Head title="Log in" />

    <AppLayout>
        <div class="mx-auto max-w-sm py-8">
            <h1 class="text-2xl font-semibold tracking-tight">Log in</h1>
            <p :class="['mt-1 mb-6 text-sm', muted]">
                Use either account. You can link the other one later.
            </p>

            <!-- Plain links: both leave the site for the provider's sign-in. -->
            <div class="flex flex-col gap-3">
                <a :href="githubLogin().url" :class="button">
                    Continue with GitHub
                </a>
                <a :href="discordLogin().url" :class="button">
                    Continue with Discord
                </a>
            </div>

            <p :class="['mt-6 text-xs', muted]">
                Submitting and editing mod listings opens pull requests on
                GitHub, so it needs a linked GitHub account.
            </p>
        </div>
    </AppLayout>
</template>
