<script setup lang="ts">
import { Head, Link, useForm, usePage } from '@inertiajs/vue3';
import FormField from '@/components/FormField.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { accounts } from '@/routes';
import { update } from '@/routes/settings/notifications';

const props = defineProps<{
    email: string | null;
    confirmed: boolean;
    pending: boolean;
    frequency: 'off' | 'daily' | 'weekly' | 'monthly';
    following: number;
}>();

const page = usePage();

const form = useForm({ email: props.email ?? '', frequency: props.frequency });

const frequencies = [
    { value: 'off', label: 'Off', hint: 'Only the bell on the site.' },
    {
        value: 'daily',
        label: 'Daily',
        hint: 'One email a day with new releases.',
    },
    { value: 'weekly', label: 'Weekly', hint: 'One email a week.' },
    { value: 'monthly', label: 'Monthly', hint: 'One email a month.' },
] as const;

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'flex flex-col gap-5 rounded-lg bg-white p-5 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
const input =
    'w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] aria-invalid:border-[#f53003] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC] dark:aria-invalid:border-[#FF4433]';
const option =
    'flex cursor-pointer items-start gap-2.5 rounded-md px-2 py-1.5 text-sm hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]';
</script>

<template>
    <Head title="Notification settings" />

    <AppLayout>
        <div class="mx-auto max-w-2xl">
            <h1 class="text-2xl font-semibold tracking-tight">
                Notification settings
            </h1>
            <p :class="['mt-1 mb-6 text-sm', muted]">
                New releases of the {{ following }} mods, vehicles and saves you
                follow show up in the bell. An email digest can gather them too.
                <Link :href="accounts()" class="underline underline-offset-4"
                    >Linked accounts</Link
                >
            </p>

            <p
                v-if="page.flash.status"
                role="status"
                class="mb-4 rounded-md bg-accent-wash p-3 text-sm text-accent"
            >
                {{ page.flash.status }}
            </p>

            <form
                :class="card"
                @submit.prevent="
                    form.put(update().url, { preserveScroll: true })
                "
            >
                <h2 class="font-semibold">Email digest</h2>

                <FormField
                    label="Email"
                    for="email"
                    :error="form.errors.email"
                    :hint="
                        confirmed
                            ? 'Confirmed.'
                            : pending
                              ? 'Waiting for you to click the link we sent. Saving again sends a new one if you change it.'
                              : 'We send a link to confirm it before any digest.'
                    "
                >
                    <input
                        id="email"
                        v-model="form.email"
                        type="email"
                        autocomplete="email"
                        :aria-invalid="!!form.errors.email"
                        :class="input"
                    />
                </FormField>

                <fieldset>
                    <legend class="mb-1.5 text-sm font-medium">
                        How often
                    </legend>
                    <label
                        v-for="item in frequencies"
                        :key="item.value"
                        :class="option"
                    >
                        <input
                            v-model="form.frequency"
                            type="radio"
                            name="frequency"
                            :value="item.value"
                            class="mt-0.5"
                        />
                        <span>
                            {{ item.label }}
                            <span :class="['block text-xs', muted]">{{
                                item.hint
                            }}</span>
                        </span>
                    </label>
                </fieldset>

                <div>
                    <button
                        type="submit"
                        :disabled="form.processing"
                        class="rounded-md bg-[#1b1b18] px-5 py-2.5 text-sm font-medium text-white hover:bg-black disabled:opacity-50 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                    >
                        Save
                    </button>
                </div>
            </form>
        </div>
    </AppLayout>
</template>
