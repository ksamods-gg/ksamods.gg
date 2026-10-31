<script setup lang="ts">
import { Head, useForm, usePage } from '@inertiajs/vue3';
import { computed } from 'vue';
import FormField from '@/components/FormField.vue';
import SettingsLayout from '@/layouts/SettingsLayout.vue';
import { t } from '@/lib/i18n';
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

const frequencies = computed(() => [
    { value: 'off', label: t('Off'), hint: t('Only the bell on the site.') },
    {
        value: 'daily',
        label: t('Daily'),
        hint: t('One email a day with new releases.'),
    },
    { value: 'weekly', label: t('Weekly'), hint: t('One email a week.') },
    { value: 'monthly', label: t('Monthly'), hint: t('One email a month.') },
]);

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const heading =
    'mb-4 border-b border-[#19140014] pb-2 text-xl font-semibold tracking-tight dark:border-[#3E3E3A]/60';
const input =
    'w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] aria-invalid:border-[#f53003] sm:max-w-sm dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC] dark:aria-invalid:border-[#FF4433]';
const option =
    'flex cursor-pointer items-start gap-2.5 rounded-md py-1.5 text-sm';
</script>

<template>
    <Head :title="$t('Notification settings')" />

    <SettingsLayout>
        <section>
            <h1 :class="heading">{{ $t('Notifications') }}</h1>
            <p :class="['text-sm', muted]">
                {{
                    following === 1
                        ? $t(
                              'New releases of the :count mod, vehicle or save you follow, replies, mentions and news about your shared listings show up in the bell. Follow something with the Follow button on its page.',
                              { count: following },
                          )
                        : $t(
                              'New releases of the :count mods, vehicles and saves you follow, replies, mentions and news about your shared listings show up in the bell. Follow something with the Follow button on its page.',
                              { count: following },
                          )
                }}
            </p>
        </section>

        <section>
            <h2 :class="heading">{{ $t('Email digest') }}</h2>

            <p
                v-if="page.flash.status"
                role="status"
                class="mb-4 rounded-md bg-accent-wash p-3 text-sm text-accent"
            >
                {{ page.flash.status }}
            </p>

            <form
                class="flex flex-col gap-6"
                @submit.prevent="
                    form.put(update().url, { preserveScroll: true })
                "
            >
                <FormField
                    :label="$t('Email')"
                    for="email"
                    :error="form.errors.email"
                    :hint="
                        confirmed
                            ? $t('Confirmed.')
                            : pending
                              ? $t(
                                    'Waiting for you to click the link we sent. Saving another address sends a new one.',
                                )
                              : $t(
                                    'We send a link to confirm it before any digest.',
                                )
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
                    <legend class="mb-1 text-sm font-medium">
                        {{ $t('How often') }}
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
                        {{ $t('Update digest') }}
                    </button>
                </div>
            </form>
        </section>
    </SettingsLayout>
</template>
