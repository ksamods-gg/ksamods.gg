<script setup lang="ts">
import { Form, Head } from '@inertiajs/vue3';
import FormField from '@/components/FormField.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { pullRequests } from '@/routes/github';
import { store, update } from '@/routes/listings';
import { show } from '@/routes/mods';
import type { ListingValues } from '@/types';

const props = defineProps<{
    // The listing being edited, or null for a new one.
    listing: ListingValues | null;
    canOpenPullRequests: boolean;
    connectFailed: boolean;
    githubLogin: string | null;
    tags: { tag: string; name: string }[];
    gameVersions: string[];
    loaders: string[];
}>();

const input =
    'w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] aria-invalid:border-[#f53003] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC] dark:aria-invalid:border-[#FF4433]';
const card =
    'flex flex-col gap-5 rounded-lg bg-white p-5 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const button =
    'inline-flex rounded-md bg-[#1b1b18] px-5 py-2.5 text-sm font-medium text-white hover:bg-black disabled:opacity-60 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white';

const title = props.listing ? `Edit ${props.listing.name}` : 'Publish your mod';
const action = props.listing ? update.form(props.listing.id) : store.form();
</script>

<template>
    <Head :title="title" />

    <AppLayout>
        <div class="mx-auto max-w-3xl">
            <a
                v-if="listing"
                :href="show(listing.id).url"
                :class="['text-sm hover:underline', muted]"
                >← {{ listing.name }}</a
            >
            <h1 class="text-2xl font-semibold tracking-tight">{{ title }}</h1>
            <p :class="['mt-1 mb-6 text-sm', muted]">
                This opens a pull request from your GitHub account that
                {{
                    listing ? 'changes this listing in' : 'adds your listing to'
                }}
                the
                <a
                    href="https://github.com/KSAModding/content-index"
                    target="_blank"
                    rel="noopener noreferrer"
                    class="underline underline-offset-4"
                    >KSA content index</a
                >.
                <template v-if="listing">
                    Images, dependencies and other details this form does not
                    show stay as they are.
                </template>
                <template v-else>
                    Once it merges, your mod shows up here and in every other
                    client that reads the index.
                </template>
            </p>

            <section v-if="!canOpenPullRequests" :class="card">
                <div>
                    <h2 class="font-semibold">Connect GitHub</h2>
                    <p :class="['mt-1 text-sm', muted]">
                        To open the pull request for you, KSA Mods needs
                        permission to fork the index and open pull requests from
                        your account. GitHub calls this access to your public
                        repositories. You are only asked once.
                    </p>
                </div>
                <p
                    v-if="connectFailed"
                    role="alert"
                    class="rounded-md bg-[#fff2f2] p-3 text-sm text-[#f53003] dark:bg-[#1D0002] dark:text-[#FF4433]"
                >
                    GitHub did not grant access to your public repositories, so
                    the pull request cannot be opened. Please try again later.
                </p>
                <div>
                    <a :href="pullRequests().url" :class="button">
                        Connect GitHub
                    </a>
                </div>
            </section>

            <Form
                v-else
                v-bind="action"
                v-slot="{ errors, processing }"
                class="flex flex-col gap-6"
            >
                <p
                    v-if="errors.pull_request"
                    role="alert"
                    class="rounded-md bg-[#fff2f2] p-3 text-sm text-[#f53003] dark:bg-[#1D0002] dark:text-[#FF4433]"
                >
                    {{ errors.pull_request }}
                </p>

                <section :class="card">
                    <h2 class="font-semibold">About your mod</h2>
                    <FormField
                        label="Id"
                        for="id"
                        required
                        :hint="
                            listing
                                ? 'An id cannot change.'
                                : 'The folder name your mod installs as, such as MeasureTools. It cannot change later.'
                        "
                        :error="errors.id"
                    >
                        <!-- Disabled when editing, so it is not sent: the address names the listing. -->
                        <input
                            id="id"
                            name="id"
                            required
                            :value="listing?.id"
                            :disabled="!!listing"
                            :aria-invalid="!!errors.id"
                            :class="[input, 'disabled:opacity-60']"
                        />
                    </FormField>
                    <FormField
                        label="Name"
                        for="name"
                        required
                        :error="errors.name"
                    >
                        <input
                            id="name"
                            name="name"
                            :value="listing?.name ?? undefined"
                            required
                            :aria-invalid="!!errors.name"
                            :class="input"
                        />
                    </FormField>
                    <FormField
                        label="Authors"
                        for="authors"
                        required
                        hint="Display names, separated by commas."
                        :error="errors.authors"
                    >
                        <input
                            id="authors"
                            name="authors"
                            :value="listing?.authors ?? undefined"
                            required
                            :aria-invalid="!!errors.authors"
                            :class="input"
                        />
                    </FormField>
                    <FormField
                        label="Summary"
                        for="abstract"
                        required
                        hint="One sentence that shows on the mod's card."
                        :error="errors.abstract"
                    >
                        <input
                            id="abstract"
                            name="abstract"
                            :value="listing?.abstract ?? undefined"
                            required
                            maxlength="300"
                            :aria-invalid="!!errors.abstract"
                            :class="input"
                        />
                    </FormField>
                    <FormField
                        label="Description"
                        for="description"
                        hint="Markdown. Images need image records, which you can add to the file in the pull request."
                        :error="errors.description"
                    >
                        <textarea
                            id="description"
                            name="description"
                            :value="listing?.description ?? undefined"
                            rows="10"
                            :aria-invalid="!!errors.description"
                            :class="[input, 'font-mono']"
                        />
                    </FormField>
                    <FormField
                        label="License"
                        for="license"
                        required
                        hint="An SPDX expression, such as MIT or GPL-2.0-only AND CC-BY-SA-4.0."
                        :error="errors.license"
                    >
                        <input
                            id="license"
                            name="license"
                            :value="listing?.license ?? undefined"
                            required
                            :aria-invalid="!!errors.license"
                            :class="input"
                        />
                    </FormField>
                    <fieldset>
                        <legend class="mb-2 text-sm font-medium">
                            Tags
                            <span :class="['font-normal', muted]"
                                >(optional)</span
                            >
                        </legend>
                        <div class="flex flex-wrap gap-x-5 gap-y-2">
                            <label
                                v-for="tag in tags"
                                :key="tag.tag"
                                class="flex items-center gap-2 text-sm"
                            >
                                <input
                                    type="checkbox"
                                    name="tags[]"
                                    :value="tag.tag"
                                    :checked="listing?.tags.includes(tag.tag)"
                                />
                                {{ tag.name }}
                            </label>
                        </div>
                    </fieldset>
                </section>

                <section :class="card">
                    <div>
                        <h2 class="font-semibold">Releases</h2>
                        <p :class="['mt-1 text-sm', muted]">
                            Where you publish releases. The index picks up new
                            releases from there on its own. Give at least one.
                        </p>
                    </div>
                    <div class="grid gap-5 sm:grid-cols-2">
                        <FormField
                            label="GitHub repository"
                            for="github"
                            hint="As owner/repository."
                            :error="errors.github"
                        >
                            <input
                                id="github"
                                name="github"
                                :value="listing?.github ?? undefined"
                                placeholder="owner/repository"
                                :aria-invalid="!!errors.github"
                                :class="input"
                            />
                        </FormField>
                        <FormField
                            label="SpaceDock mod id"
                            for="spacedock"
                            hint="The number in your SpaceDock mod's address."
                            :error="errors.spacedock"
                        >
                            <input
                                id="spacedock"
                                name="spacedock"
                                :value="listing?.spacedock ?? undefined"
                                type="number"
                                min="1"
                                :aria-invalid="!!errors.spacedock"
                                :class="input"
                            />
                        </FormField>
                    </div>
                    <FormField
                        label="Release authority"
                        for="authority"
                        hint="Only needed when you give both: the host that decides which releases exist."
                        :error="errors.authority"
                    >
                        <select
                            id="authority"
                            name="authority"
                            :aria-invalid="!!errors.authority"
                            :class="input"
                        >
                            <option value="">Not needed</option>
                            <option
                                value="github"
                                :selected="listing?.authority === 'github'"
                            >
                                GitHub
                            </option>
                            <option
                                value="spacedock"
                                :selected="listing?.authority === 'spacedock'"
                            >
                                SpaceDock
                            </option>
                        </select>
                    </FormField>
                </section>

                <section :class="card">
                    <h2 class="font-semibold">Links</h2>
                    <FormField
                        label="Forum thread"
                        for="forums"
                        required
                        hint="Your mod's thread on forums.ahwoo.com. The index requires one."
                        :error="errors.forums"
                    >
                        <input
                            id="forums"
                            name="forums"
                            :value="listing?.forums ?? undefined"
                            type="url"
                            required
                            placeholder="https://forums.ahwoo.com/threads/my-mod.123/"
                            :aria-invalid="!!errors.forums"
                            :class="input"
                        />
                    </FormField>
                    <FormField
                        label="Source code"
                        for="repository"
                        :error="errors.repository"
                    >
                        <input
                            id="repository"
                            name="repository"
                            :value="listing?.repository ?? undefined"
                            type="url"
                            :aria-invalid="!!errors.repository"
                            :class="input"
                        />
                    </FormField>
                    <FormField
                        label="Issue tracker"
                        for="bugtracker"
                        :error="errors.bugtracker"
                    >
                        <input
                            id="bugtracker"
                            name="bugtracker"
                            :value="listing?.bugtracker ?? undefined"
                            type="url"
                            :aria-invalid="!!errors.bugtracker"
                            :class="input"
                        />
                    </FormField>
                </section>

                <section :class="card">
                    <h2 class="font-semibold">Compatibility</h2>
                    <FormField
                        label="Oldest game version it works with"
                        for="game_min"
                        required
                        :error="errors.game_min"
                    >
                        <select
                            id="game_min"
                            name="game_min"
                            required
                            :aria-invalid="!!errors.game_min"
                            :class="input"
                        >
                            <option
                                v-for="version in gameVersions"
                                :key="version"
                                :value="version"
                                :selected="version === listing?.game_min"
                            >
                                {{ version }}
                            </option>
                        </select>
                    </FormField>
                    <!-- A mod loader names no loader of its own. -->
                    <div
                        v-if="(listing?.type ?? 'mod') === 'mod'"
                        class="grid gap-5 sm:grid-cols-2"
                    >
                        <FormField
                            label="Mod loader"
                            for="loader_id"
                            hint="For code mods."
                            :error="errors.loader_id"
                        >
                            <select
                                id="loader_id"
                                name="loader_id"
                                :aria-invalid="!!errors.loader_id"
                                :class="input"
                            >
                                <option value="">None</option>
                                <option
                                    v-for="loader in loaders"
                                    :key="loader"
                                    :value="loader"
                                    :selected="loader === listing?.loader_id"
                                >
                                    {{ loader }}
                                </option>
                            </select>
                        </FormField>
                        <FormField
                            label="Oldest loader version"
                            for="loader_min"
                            hint="Needed with a loader, such as 0.4.5."
                            :error="errors.loader_min"
                        >
                            <input
                                id="loader_min"
                                name="loader_min"
                                :value="listing?.loader_min ?? undefined"
                                :aria-invalid="!!errors.loader_min"
                                :class="input"
                            />
                        </FormField>
                    </div>
                </section>

                <section
                    class="rounded-lg border border-dashed border-[#19140035] p-5 text-sm dark:border-[#3E3E3A]"
                >
                    <h2 class="font-semibold">
                        So the pull request merges itself
                    </h2>
                    <p :class="['mt-1', muted]">
                        The index checks that you control the GitHub release
                        repository. A repository in your own account passes as
                        it is. For one owned by an organization, we add the
                        topic
                        <code
                            class="font-mono text-[#1b1b18] dark:text-[#EDEDEC]"
                            >ksa-index-{{
                                (githubLogin ?? 'your-username').toLowerCase()
                            }}</code
                        >
                        to it for you, which needs you to be able to edit the
                        repository's settings. For releases on SpaceDock only,
                        that is the GitHub repository your SpaceDock mod links
                        to as its source code. If we cannot add the topic, the
                        pull request's bot tells you what to add.
                    </p>
                </section>

                <div class="flex items-center gap-4">
                    <button
                        type="submit"
                        :disabled="processing"
                        :class="button"
                    >
                        {{
                            processing
                                ? 'Opening pull request…'
                                : 'Open pull request'
                        }}
                    </button>
                    <p v-if="processing" :class="['text-sm', muted]">
                        This takes a few seconds while GitHub sets up your fork.
                    </p>
                </div>
            </Form>
        </div>
    </AppLayout>
</template>
