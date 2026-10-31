<script setup lang="ts">
import { Head, Link, useForm, usePage } from '@inertiajs/vue3';
import FormField from '@/components/FormField.vue';
import SettingsLayout from '@/layouts/SettingsLayout.vue';
import { update } from '@/routes/settings/profile';

const props = defineProps<{
    enabled: boolean;
    username: string | null;
    bio: string | null;
    links: string[];
    showAccounts: boolean;
    sections: Record<
        'vehicles' | 'saves' | 'mods' | 'reviews' | 'comments',
        boolean
    >;
    url: string | null;
}>();

const page = usePage();

const form = useForm({
    enabled: props.enabled,
    username: props.username ?? '',
    bio: props.bio ?? '',
    // Four slots, like GitHub's social accounts.
    links: [0, 1, 2, 3].map((i) => props.links[i] ?? ''),
    showAccounts: props.showAccounts,
    sections: { ...props.sections },
});

const sectionLabels = {
    vehicles: 'Shared vehicles',
    saves: 'Shared saves',
    mods: 'My mods',
    reviews: 'Reviews',
    comments: 'Comments',
} as const;

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const heading =
    'mb-4 border-b border-[#19140014] pb-2 text-xl font-semibold tracking-tight dark:border-[#3E3E3A]/60';
const input =
    'w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] aria-invalid:border-[#f53003] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC] dark:aria-invalid:border-[#FF4433]';
const check = 'flex cursor-pointer items-start gap-2.5 py-1 text-sm';
</script>

<template>
    <Head title="Public profile" />

    <SettingsLayout>
        <form
            class="flex flex-col gap-10"
            @submit.prevent="form.put(update().url, { preserveScroll: true })"
        >
            <section>
                <h1 :class="heading">Public profile</h1>

                <p
                    v-if="page.flash.status"
                    role="status"
                    class="mb-4 rounded-md bg-accent-wash p-3 text-sm text-accent"
                >
                    {{ page.flash.status }}
                    <Link
                        v-if="url"
                        :href="url"
                        class="underline underline-offset-4"
                        >View it</Link
                    >
                </p>

                <div class="flex flex-col gap-6">
                    <label :class="check">
                        <input
                            v-model="form.enabled"
                            type="checkbox"
                            class="mt-0.5"
                        />
                        <span>
                            <span class="font-medium"
                                >Turn on my public profile</span
                            >
                            <span :class="['block text-xs', muted]">
                                Anyone can then see it, and your name on reviews
                                and comments links to it. Off, the address shows
                                nothing.
                            </span>
                        </span>
                    </label>

                    <FormField
                        label="Username"
                        for="username"
                        :required="form.enabled"
                        :error="form.errors.username"
                        hint="3 to 30 letters, digits, - or _. Changing it changes your profile's address."
                    >
                        <div
                            class="flex w-full items-center rounded-md border border-[#19140035] bg-white text-sm focus-within:border-[#1b1b18] sm:max-w-sm dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus-within:border-[#EDEDEC]"
                        >
                            <span :class="['pl-3', muted]">ksamods.gg/u/</span>
                            <input
                                id="username"
                                v-model="form.username"
                                maxlength="30"
                                autocomplete="username"
                                :aria-invalid="!!form.errors.username"
                                class="min-w-0 flex-1 bg-transparent py-2 pr-3 outline-none"
                            />
                        </div>
                    </FormField>

                    <FormField
                        label="Bio"
                        for="bio"
                        :error="form.errors.bio"
                        hint="A few words about yourself, up to 300 characters."
                    >
                        <textarea
                            id="bio"
                            v-model="form.bio"
                            rows="3"
                            maxlength="300"
                            :class="[input, 'sm:max-w-lg']"
                        />
                    </FormField>

                    <fieldset class="flex flex-col gap-2">
                        <legend class="mb-1.5 text-sm font-medium">
                            Links
                            <span :class="['font-normal', muted]"
                                >(optional)</span
                            >
                        </legend>
                        <div
                            v-for="(_, i) in form.links"
                            :key="i"
                            class="flex items-center gap-2"
                        >
                            <svg
                                viewBox="0 0 24 24"
                                fill="none"
                                stroke="currentColor"
                                stroke-width="1.75"
                                stroke-linecap="round"
                                stroke-linejoin="round"
                                aria-hidden="true"
                                :class="['size-4 shrink-0', muted]"
                            >
                                <path
                                    d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"
                                />
                            </svg>
                            <label :for="`link-${i}`" class="sr-only"
                                >Link {{ i + 1 }}</label
                            >
                            <input
                                :id="`link-${i}`"
                                v-model="form.links[i]"
                                type="url"
                                :placeholder="`Link ${i + 1}, such as a website or YouTube channel`"
                                :aria-invalid="
                                    !!(form.errors as Record<string, string>)[
                                        `links.${i}`
                                    ]
                                "
                                :class="[input, 'sm:max-w-lg']"
                            />
                        </div>
                        <p
                            v-for="(_, i) in form.links"
                            v-show="
                                (form.errors as Record<string, string>)[
                                    `links.${i}`
                                ]
                            "
                            :key="`error-${i}`"
                            class="text-xs text-[#f53003] dark:text-[#FF4433]"
                        >
                            Link {{ i + 1 }} must be a web address starting with
                            https://.
                        </p>
                    </fieldset>

                    <label :class="check">
                        <input
                            v-model="form.showAccounts"
                            type="checkbox"
                            class="mt-0.5"
                        />
                        <span>
                            <span class="font-medium"
                                >Show my GitHub and Discord names</span
                            >
                            <span :class="['block text-xs', muted]">
                                The accounts linked to ksamods.gg, on your
                                profile.
                            </span>
                        </span>
                    </label>
                </div>
            </section>

            <section>
                <h2 :class="heading">What your profile shows</h2>
                <p :class="['mb-3 text-sm', muted]">
                    Anything you turn off is left off your profile. Reviews and
                    comments stay on the mod pages either way.
                </p>
                <label
                    v-for="(label, section) in sectionLabels"
                    :key="section"
                    :class="check"
                >
                    <input
                        v-model="form.sections[section]"
                        type="checkbox"
                        class="mt-0.5"
                    />
                    {{ label }}
                </label>
            </section>

            <div>
                <button
                    type="submit"
                    :disabled="form.processing"
                    class="rounded-md bg-[#1b1b18] px-5 py-2.5 text-sm font-medium text-white hover:bg-black disabled:opacity-50 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                >
                    Update profile
                </button>
            </div>
        </form>
    </SettingsLayout>
</template>
