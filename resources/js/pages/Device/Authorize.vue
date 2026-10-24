<script setup lang="ts">
import { Head, Link, useForm, usePage } from '@inertiajs/vue3';
import AppLayout from '@/layouts/AppLayout.vue';
import { policy } from '@/routes';
import { approve, deny } from '@/routes/passport/device/authorizations';

/**
 * Asks the player to approve the client that showed them the code. Approving lets it share
 * vehicles and saves as them, and counts as their consent to the public index (RFC 0094).
 */
const props = defineProps<{
    // The name the client gave, such as borea.
    client: string;
    userCode: string;
    authToken: string;
}>();

const page = usePage();
const answer = useForm({ auth_token: props.authToken });

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'rounded-lg bg-white p-6 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
</script>

<template>
    <Head title="Approve sign-in" />

    <AppLayout>
        <div class="mx-auto max-w-md">
            <h1 class="text-2xl font-semibold tracking-tight">
                Sign in {{ client }}?
            </h1>
            <p :class="['mt-1 mb-6 text-sm', muted]">
                Code
                <span
                    class="font-mono tracking-widest text-[#1b1b18] dark:text-[#EDEDEC]"
                    >{{ userCode }}</span
                >
                — only approve if your client shows the same code.
            </p>

            <section :class="[card, 'flex flex-col gap-4 text-sm']">
                <p>
                    <strong>{{ client }}</strong> will be able to share vehicles
                    and saves on ksamods.gg as
                    <strong>{{ page.props.auth.user?.name }}</strong
                    >.
                </p>
                <p :class="muted">
                    What you publish goes into the public KSA content index,
                    which keeps its history: the listing text stays visible in
                    the index's history even after you change or remove it. Read
                    the
                    <Link :href="policy()" class="underline underline-offset-4"
                        >host policy</Link
                    >.
                </p>
                <div class="flex flex-wrap gap-3">
                    <button
                        type="button"
                        :disabled="answer.processing"
                        class="rounded-md bg-[#1b1b18] px-5 py-2.5 font-medium text-white hover:bg-black disabled:opacity-60 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                        @click="answer.post(approve().url)"
                    >
                        Approve
                    </button>
                    <button
                        type="button"
                        :disabled="answer.processing"
                        class="rounded-md border border-[#19140035] px-5 py-2.5 font-medium hover:border-[#1915014a] disabled:opacity-60 dark:border-[#3E3E3A] dark:hover:border-[#62605b]"
                        @click="answer.delete(deny().url)"
                    >
                        Deny
                    </button>
                </div>
            </section>
        </div>
    </AppLayout>
</template>
