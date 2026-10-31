<script setup lang="ts">
import { Head, Link, useForm, usePage } from '@inertiajs/vue3';
import FormField from '@/components/FormField.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { show, update } from '@/routes/shares';
import type { ShareType } from '@/types';

const props = defineProps<{
    share: {
        id: string;
        type: ShareType;
        name: string;
        abstract: string;
        description: string | null;
        license: string;
        tags: string;
    };
}>();

const types = props.share.type === 'vehicle' ? 'vehicles' : 'saves';

const form = useForm({
    name: props.share.name,
    abstract: props.share.abstract,
    description: props.share.description ?? '',
    license: props.share.license,
    tags: props.share.tags,
});
const page = usePage();

const submit = () => form.put(update({ types, id: props.share.id }).url);

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'flex flex-col gap-5 rounded-lg bg-white p-5 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
const input =
    'w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] aria-invalid:border-[#f53003] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC] dark:aria-invalid:border-[#FF4433]';
</script>

<template>
    <Head :title="`Edit ${share.name}`" />

    <AppLayout>
        <div class="mx-auto max-w-3xl">
            <Link
                :href="show({ types, id: share.id })"
                :class="['text-sm hover:underline', muted]"
            >
                ← {{ share.name }}
            </Link>
            <h1 class="mt-3 text-2xl font-semibold tracking-tight">
                Edit the listing
            </h1>
            <p :class="['mt-1 mb-6 text-sm', muted]">
                Saving proposes the change to the index by pull request, which
                merges by itself once its checks pass. The index keeps the
                listing's history, so earlier text stays visible there.
            </p>

            <form class="flex flex-col gap-6" @submit.prevent="submit">
                <p
                    v-if="page.props.errors.listing"
                    role="alert"
                    class="rounded-md bg-[#fff2f2] p-3 text-sm text-[#f53003] dark:bg-[#1D0002] dark:text-[#FF4433]"
                >
                    {{ page.props.errors.listing }}
                </p>

                <section :class="card">
                    <FormField label="Name" for="name" required>
                        <input
                            id="name"
                            v-model="form.name"
                            required
                            maxlength="200"
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
                            rows="8"
                            :class="input"
                        />
                    </FormField>
                    <div class="grid gap-5 sm:grid-cols-2">
                        <FormField
                            label="License"
                            for="license"
                            required
                            hint="An SPDX id, such as CC-BY-4.0. Releases already out keep the license they came with."
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

                <div class="flex flex-wrap items-center gap-3">
                    <button
                        type="submit"
                        :disabled="form.processing"
                        class="rounded-md bg-[#1b1b18] px-5 py-2.5 text-sm font-medium text-white hover:bg-black disabled:opacity-50 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                    >
                        Save and propose
                    </button>
                    <Link
                        :href="show({ types, id: share.id })"
                        :class="['text-sm hover:underline', muted]"
                        >Cancel</Link
                    >
                </div>
            </form>
        </div>
    </AppLayout>
</template>
