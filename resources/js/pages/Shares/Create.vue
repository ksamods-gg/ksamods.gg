<script setup lang="ts">
import { Head, Link, useForm, usePage } from '@inertiajs/vue3';
import { computed } from 'vue';
import FormField from '@/components/FormField.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { formatBytes } from '@/lib/format';
import { policy } from '@/routes';
import { upload } from '@/routes/shares';
import type { ShareCheck, ShareType } from '@/types';

const props = defineProps<{
    // Whether the player already confirmed that listings go into the public index.
    consented: boolean;
    // A listing to add a version to, from its page.
    listing: string | null;
    mine: { id: string; name: string; type: ShareType }[];
    mods: { id: string; name: string }[];
    maxBytes: Record<ShareType, number>;
    types: ShareType[];
}>();

const form = useForm<{
    archive: File | null;
    listing: string;
    consent: boolean;
    dependencies: { id: string; kind: string; min: string }[];
}>({
    archive: null,
    listing: props.listing ?? '',
    consent: false,
    dependencies: [],
});

// The checks of a refused archive come back as JSON beside the message.
const page = usePage();
const checks = computed<ShareCheck[]>(() => {
    try {
        return page.props.errors.checks
            ? JSON.parse(page.props.errors.checks)
            : [];
    } catch {
        return [];
    }
});

function submit() {
    form.post(upload().url, { forceFormData: true });
}

const kinds = [
    ['required', 'Required'],
    ['recommends', 'Recommended'],
    ['optional', 'Optional'],
    ['suggests', 'Suggested'],
    ['conflict', 'Conflicts with'],
] as const;

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'flex flex-col gap-5 rounded-lg bg-white p-5 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
const input =
    'w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] aria-invalid:border-[#f53003] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC] dark:aria-invalid:border-[#FF4433]';
</script>

<template>
    <Head title="Share a vehicle or save" />

    <AppLayout>
        <div class="mx-auto max-w-3xl">
            <h1 class="text-2xl font-semibold tracking-tight">
                Share a vehicle or save
            </h1>
            <p :class="['mt-1 mb-6 text-sm', muted]">
                Upload the folder the game wrote, as a .zip. It is checked
                first; then you confirm the version and the listing, and it goes
                to the KSA content index. Sharing from Borea works too.
            </p>

            <form class="flex flex-col gap-6" @submit.prevent="submit">
                <section
                    v-if="!consented"
                    :class="[card, 'bg-accent-wash dark:bg-accent-wash']"
                >
                    <div>
                        <h2 class="font-semibold">Before your first upload</h2>
                        <p class="mt-1 text-sm">
                            What you publish goes into the public KSA content
                            index, which keeps its history: the listing text
                            stays visible in that history even after you change
                            or remove it. Read the
                            <Link
                                :href="policy()"
                                class="underline underline-offset-4"
                                >host policy</Link
                            >
                            for takedowns and privacy.
                        </p>
                    </div>
                    <label class="flex items-start gap-2.5 text-sm">
                        <input
                            v-model="form.consent"
                            type="checkbox"
                            class="mt-0.5"
                            :aria-invalid="!!form.errors.consent"
                        />
                        I understand that the listing becomes public and keeps
                        its history.
                    </label>
                    <p
                        v-if="form.errors.consent"
                        class="-mt-3 text-xs text-[#f53003] dark:text-[#FF4433]"
                    >
                        {{ form.errors.consent }}
                    </p>
                </section>

                <section :class="card">
                    <FormField
                        v-if="mine.length"
                        label="Listing"
                        for="listing"
                        required
                        hint="A new listing, or a new version of one of yours."
                    >
                        <select
                            id="listing"
                            v-model="form.listing"
                            :class="input"
                        >
                            <option value="">A new vehicle or save</option>
                            <option
                                v-for="share in mine"
                                :key="share.id"
                                :value="share.id"
                            >
                                New version of {{ share.name }} ({{
                                    share.type
                                }})
                            </option>
                        </select>
                    </FormField>

                    <FormField
                        label="Archive"
                        for="archive"
                        required
                        :hint="`One folder with meta.toml and vehicle.xml (a vehicle) or universe.xml (a save). Up to ${formatBytes(maxBytes.vehicle)} for a vehicle and ${formatBytes(maxBytes.save)} for a save.`"
                        :error="form.errors.archive"
                    >
                        <input
                            id="archive"
                            type="file"
                            accept=".zip,application/zip"
                            required
                            :aria-invalid="!!form.errors.archive"
                            :class="[
                                input,
                                'file:mr-3 file:rounded file:border-0 file:bg-[#f4f3ef] file:px-2 file:py-1 file:text-sm dark:file:bg-[#232321] dark:file:text-[#EDEDEC]',
                            ]"
                            @change="
                                form.archive =
                                    ($event.target as HTMLInputElement)
                                        .files?.[0] ?? null
                            "
                        />
                    </FormField>

                    <ul
                        v-if="checks.length"
                        class="flex flex-col gap-1.5 rounded-md bg-[#f4f3ef] p-3 text-sm dark:bg-[#232321]"
                        aria-label="Checks"
                    >
                        <li
                            v-for="check in checks"
                            :key="check.name"
                            class="flex gap-2"
                        >
                            <span
                                aria-hidden="true"
                                :class="
                                    check.passed
                                        ? 'text-accent'
                                        : 'text-[#f53003] dark:text-[#FF4433]'
                                "
                                >{{ check.passed ? '✓' : '✗' }}</span
                            >
                            <span
                                ><span class="sr-only">{{
                                    check.passed ? 'Passed: ' : 'Failed: '
                                }}</span
                                >{{ check.message }}</span
                            >
                        </li>
                    </ul>
                </section>

                <section :class="card">
                    <div>
                        <h2 class="font-semibold">Mods it needs</h2>
                        <p :class="['mt-1 text-sm', muted]">
                            One entry per mod whose parts it uses, without the
                            game's own parts and without those mods'
                            dependencies. A client installs them before the
                            {{
                                form.listing
                                    ? 'new version'
                                    : 'vehicle or save'
                            }}.
                        </p>
                    </div>

                    <datalist id="mod-ids">
                        <option
                            v-for="mod in mods"
                            :key="mod.id"
                            :value="mod.id"
                        >
                            {{ mod.name }}
                        </option>
                    </datalist>

                    <div
                        v-for="(dependency, index) in form.dependencies"
                        :key="index"
                        class="grid gap-2 sm:grid-cols-[minmax(0,1fr)_10rem_8rem_auto]"
                    >
                        <input
                            v-model="dependency.id"
                            list="mod-ids"
                            required
                            placeholder="Mod id, such as PartsPack"
                            :aria-label="`Mod ${index + 1}`"
                            :class="input"
                        />
                        <select
                            v-model="dependency.kind"
                            :aria-label="`How mod ${index + 1} is needed`"
                            :class="input"
                        >
                            <option
                                v-for="[value, label] in kinds"
                                :key="value"
                                :value="value"
                            >
                                {{ label }}
                            </option>
                        </select>
                        <input
                            v-model="dependency.min"
                            placeholder="Min version"
                            :aria-label="`Lowest version of mod ${index + 1}`"
                            :class="[input, 'font-mono']"
                        />
                        <button
                            type="button"
                            :class="['text-sm hover:underline', muted]"
                            @click="form.dependencies.splice(index, 1)"
                        >
                            Remove
                        </button>
                    </div>
                    <div>
                        <button
                            type="button"
                            class="text-sm underline underline-offset-4"
                            @click="
                                form.dependencies.push({
                                    id: '',
                                    kind: 'required',
                                    min: '',
                                })
                            "
                        >
                            Add a mod
                        </button>
                    </div>
                </section>

                <div class="flex items-center gap-4">
                    <button
                        type="submit"
                        :disabled="form.processing"
                        class="rounded-md bg-[#1b1b18] px-5 py-2.5 text-sm font-medium text-white hover:bg-black disabled:opacity-60 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                    >
                        {{ form.processing ? 'Checking…' : 'Check archive' }}
                    </button>
                    <progress
                        v-if="form.progress"
                        :value="form.progress.percentage"
                        max="100"
                        class="h-2 flex-1 accent-accent"
                        aria-label="Upload progress"
                    />
                </div>
            </form>
        </div>
    </AppLayout>
</template>
