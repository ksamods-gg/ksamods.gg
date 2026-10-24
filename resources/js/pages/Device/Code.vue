<script setup lang="ts">
import { Form, Head } from '@inertiajs/vue3';
import AppLayout from '@/layouts/AppLayout.vue';
import { device } from '@/routes/passport';

/**
 * Where a player types the code a client such as Borea shows, to sign it in (RFC 8628).
 */
defineProps<{
    code: string | null;
    status: 'authorization-approved' | 'authorization-denied' | null;
}>();

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'rounded-lg bg-white p-6 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
</script>

<template>
    <Head title="Sign in a client" />

    <AppLayout>
        <div class="mx-auto max-w-md">
            <h1 class="text-2xl font-semibold tracking-tight">
                Sign in a client
            </h1>

            <div
                v-if="status === 'authorization-approved'"
                role="status"
                :class="[card, 'mt-6']"
            >
                <p class="font-semibold">You're signed in.</p>
                <p :class="['mt-1 text-sm', muted]">
                    Go back to the client; it continues by itself. You can close
                    this page.
                </p>
            </div>
            <div
                v-else-if="status === 'authorization-denied'"
                role="status"
                :class="[card, 'mt-6']"
            >
                <p class="font-semibold">Sign-in denied.</p>
                <p :class="['mt-1 text-sm', muted]">
                    The client was not given access to your account.
                </p>
            </div>

            <template v-else>
                <p :class="['mt-1 mb-6 text-sm', muted]">
                    Enter the code your client shows, such as Borea when you
                    share a vehicle or save.
                </p>
                <Form
                    v-bind="device.form()"
                    v-slot="{ errors }"
                    :class="[card, 'flex flex-col gap-3']"
                >
                    <label for="user_code" class="text-sm font-medium"
                        >Code</label
                    >
                    <input
                        id="user_code"
                        name="user_code"
                        :value="code ?? undefined"
                        required
                        autofocus
                        autocomplete="one-time-code"
                        autocapitalize="characters"
                        spellcheck="false"
                        :aria-invalid="!!errors.user_code"
                        class="w-full rounded-md border border-[#19140035] bg-white px-3 py-2.5 text-center font-mono text-lg tracking-[0.3em] uppercase outline-none focus:border-[#1b1b18] aria-invalid:border-[#f53003] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC] dark:aria-invalid:border-[#FF4433]"
                    />
                    <p
                        v-if="errors.user_code"
                        class="text-xs text-[#f53003] dark:text-[#FF4433]"
                    >
                        That code is not valid. Check it, or start the sign-in
                        again in the client.
                    </p>
                    <button
                        type="submit"
                        class="rounded-md bg-[#1b1b18] px-5 py-2.5 text-sm font-medium text-white hover:bg-black dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                    >
                        Continue
                    </button>
                </Form>
            </template>
        </div>
    </AppLayout>
</template>
