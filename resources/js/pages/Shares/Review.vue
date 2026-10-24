<script setup lang="ts">
import { Head, Link, useForm, usePage } from '@inertiajs/vue3';
import FormField from '@/components/FormField.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { formatDate } from '@/lib/format';
import { create, publish } from '@/routes/shares';
import type { ShareCheck, ShareDependency, ShareType } from '@/types';

/**
 * After the checks: the player confirms the version and, for a new id, writes the listing.
 */
const props = defineProps<{
    upload: string;
    type: ShareType;
    checks: ShareCheck[];
    // The proposed version: 1.0.0, or the next minor version of the listing.
    version: string;
    // The listing text to show, as TOML.
    listing: string;
    expires: string;
    expired: boolean;
    // The listing a new version is for; null for a new listing.
    listingId: string | null;
    dependencies: ShareDependency[];
    defaults: { name: string | null; authors: string };
}>();

const page = usePage();

const form = useForm({
    version: props.version,
    status: 'stable',
    id: (props.defaults.name ?? '').replace(/[^A-Za-z0-9._-]/g, ''),
    name: props.defaults.name ?? '',
    authors: props.defaults.authors,
    abstract: '',
    description: '',
    license: 'CC-BY-4.0',
    tags: '',
});

function submit() {
    form.post(publish(props.upload).url);
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'flex flex-col gap-5 rounded-lg bg-white p-5 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
const input =
    'w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] aria-invalid:border-[#f53003] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC] dark:aria-invalid:border-[#FF4433]';
</script>

<template>
    <Head title="Publish" />

    <AppLayout>
        <div class="mx-auto max-w-3xl">
            <h1 class="text-2xl font-semibold tracking-tight">
                {{
                    listingId
                        ? `A new version of ${listingId}`
                        : `Publish your ${type}`
                }}
            </h1>
            <p :class="['mt-1 mb-6 text-sm', muted]">
                The archive passed its checks. Confirm the details; the upload
                waits until {{ formatDate(expires) }}.
            </p>

            <p
                v-if="expired"
                role="alert"
                class="mb-6 rounded-md bg-[#fff2f2] p-3 text-sm text-[#f53003] dark:bg-[#1D0002] dark:text-[#FF4433]"
            >
                This upload expired.
                <Link :href="create()" class="underline underline-offset-4"
                    >Upload it again</Link
                >.
            </p>

            <form class="flex flex-col gap-6" @submit.prevent="submit">
                <p
                    v-if="page.props.errors.publish"
                    role="alert"
                    class="rounded-md bg-[#fff2f2] p-3 text-sm text-[#f53003] dark:bg-[#1D0002] dark:text-[#FF4433]"
                >
                    {{ page.props.errors.publish }}
                </p>

                <section v-if="!listingId" :class="card">
                    <h2 class="font-semibold">The listing</h2>
                    <FormField
                        label="Id"
                        for="id"
                        required
                        hint="How the index and clients name it, such as ExampleRover: letters, digits, ., _ or -. It cannot change later."
                        :error="form.errors.id"
                    >
                        <input
                            id="id"
                            v-model="form.id"
                            required
                            maxlength="64"
                            :aria-invalid="!!form.errors.id"
                            :class="[input, 'font-mono']"
                        />
                    </FormField>
                    <FormField label="Name" for="name" required>
                        <input
                            id="name"
                            v-model="form.name"
                            required
                            :class="input"
                        />
                    </FormField>
                    <FormField
                        label="Authors"
                        for="authors"
                        required
                        hint="The names you publish under, separated by commas."
                    >
                        <input
                            id="authors"
                            v-model="form.authors"
                            required
                            :class="input"
                        />
                    </FormField>
                    <FormField
                        label="Summary"
                        for="abstract"
                        required
                        hint="One sentence for its card."
                    >
                        <input
                            id="abstract"
                            v-model="form.abstract"
                            required
                            maxlength="300"
                            :class="input"
                        />
                    </FormField>
                    <FormField label="Description" for="description">
                        <textarea
                            id="description"
                            v-model="form.description"
                            rows="5"
                            :class="input"
                        />
                    </FormField>
                    <div class="grid gap-5 sm:grid-cols-2">
                        <FormField
                            label="License"
                            for="license"
                            required
                            hint="An SPDX id, such as CC-BY-4.0."
                        >
                            <input
                                id="license"
                                v-model="form.license"
                                required
                                :class="[input, 'font-mono']"
                            />
                        </FormField>
                        <FormField
                            label="Tags"
                            for="tags"
                            hint="Lowercase, comma-separated, such as rover, lander."
                        >
                            <input
                                id="tags"
                                v-model="form.tags"
                                :class="input"
                            />
                        </FormField>
                    </div>
                </section>

                <section :class="card">
                    <h2 class="font-semibold">The release</h2>
                    <div class="grid gap-5 sm:grid-cols-2">
                        <FormField
                            label="Version"
                            for="version"
                            required
                            hint="Three numbers, such as 1.0.0. A version is never used twice."
                            :error="form.errors.version"
                        >
                            <input
                                id="version"
                                v-model="form.version"
                                required
                                :aria-invalid="!!form.errors.version"
                                :class="[input, 'font-mono']"
                            />
                        </FormField>
                        <FormField label="Status" for="status" required>
                            <select
                                id="status"
                                v-model="form.status"
                                :class="input"
                            >
                                <option value="stable">Stable</option>
                                <option value="testing">Testing</option>
                                <option value="dev">Dev</option>
                            </select>
                        </FormField>
                    </div>
                </section>

                <details :class="card">
                    <summary class="cursor-pointer font-semibold">
                        Checks and listing text
                    </summary>
                    <ul class="flex flex-col gap-1.5 text-sm">
                        <li
                            v-for="check in checks"
                            :key="check.name"
                            class="flex gap-2"
                        >
                            <span aria-hidden="true" class="text-accent"
                                >✓</span
                            >
                            {{ check.message }}
                        </li>
                    </ul>
                    <p v-if="dependencies.length" :class="['text-sm', muted]">
                        Needs
                        {{
                            dependencies.map((d) => d.name ?? d.id).join(', ')
                        }}.
                    </p>
                    <pre
                        class="overflow-x-auto rounded-md bg-[#f4f3ef] p-3 font-mono text-xs dark:bg-[#232321]"
                        >{{ listing }}</pre>
                </details>

                <div>
                    <button
                        type="submit"
                        :disabled="form.processing || expired"
                        class="rounded-md bg-[#1b1b18] px-5 py-2.5 text-sm font-medium text-white hover:bg-black disabled:opacity-60 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                    >
                        {{ form.processing ? 'Publishing…' : 'Publish' }}
                    </button>
                </div>
            </form>
        </div>
    </AppLayout>
</template>
