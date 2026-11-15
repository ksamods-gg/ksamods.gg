<script setup lang="ts">
import { Form, Head } from '@inertiajs/vue3';
import { computed, onMounted, ref } from 'vue';
import FormField from '@/components/FormField.vue';
import ModIcon from '@/components/ModIcon.vue';
import PublishTabs from '@/components/PublishTabs.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { t } from '@/lib/i18n';
import { pullRequests } from '@/routes/github';
import { check, show, store } from '@/routes/packs';

type Release = { version: string; status: string };
type PickableMod = {
    id: string;
    name: string;
    icon: string | null;
    releases: Release[];
};
// added: the form added it because another pin needs it.
type Pin = { id: string; version: string; added?: boolean };

const props = defineProps<{
    // The newest version of the pack, for its next version; null for a new pack.
    pack: {
        id: string;
        version: string;
        previous: string;
        name: string;
        authors: string;
        abstract: string;
        description: string;
        license: string;
        tags: string[];
        forums: string;
        repository: string;
        game_min: string | null;
        icon: string | null;
        mods: { id: string; version: string }[];
    } | null;
    canOpenPullRequests: boolean;
    mods: PickableMod[];
    tags: { tag: string; name: string }[];
    gameVersions: string[];
}>();

const byId = computed(
    () => new Map(props.mods.map((mod) => [mod.id.toLowerCase(), mod])),
);
const pins = ref<Pin[]>(props.pack?.mods.map((pin) => ({ ...pin })) ?? []);
const gameMin = ref(props.pack?.game_min ?? '');
const choice = ref('');
const checking = ref(false);
const errors = ref<string[]>([]);
const notes = ref<string[]>([]);

const offered = computed(() =>
    props.mods.filter(
        (mod) =>
            !pins.value.some(
                (pin) => pin.id.toLowerCase() === mod.id.toLowerCase(),
            ),
    ),
);

// The newest stable release, else the newest one: what a new pin starts at.
function defaultVersion(mod: PickableMod): string {
    return (
        mod.releases.find((release) => release.status === 'stable') ??
        mod.releases[0]
    ).version;
}

function add(): void {
    const mod = byId.value.get(choice.value.toLowerCase());

    if (mod) {
        pins.value.push({ id: mod.id, version: defaultVersion(mod) });
        choice.value = '';
        void recheck();
    }
}

function remove(index: number): void {
    pins.value.splice(index, 1);
    // The added dependencies go too; the check adds back what the other pins still need.
    pins.value = pins.value.filter((pin) => !pin.added);
    void recheck();
}

// Asks the server, which runs the index's own rules: it adds the dependencies the pins need,
// and says what is still wrong in the index's words.
async function recheck(): Promise<void> {
    checking.value = true;
    const query = new URLSearchParams();
    pins.value.forEach((pin, index) => {
        query.append(`mods[${index}][id]`, pin.id);
        query.append(`mods[${index}][version]`, pin.version);
    });

    try {
        const response = await fetch(`${check().url}?${query}`, {
            headers: { Accept: 'application/json' },
        });
        const result: {
            errors: string[];
            notes: string[];
            adds: { id: string; version: string }[];
            unresolved: string[];
            game_min: string | null;
        } = await response.json();

        for (const pin of result.adds) {
            pins.value.push({ ...pin, added: true });
        }

        errors.value = result.errors;
        notes.value = [...result.notes, ...result.unresolved];

        // The pinned releases need at least this game build.
        if (
            result.game_min &&
            props.gameVersions.indexOf(result.game_min) <
                props.gameVersions.indexOf(gameMin.value || '')
        ) {
            gameMin.value = result.game_min;
        } else if (result.game_min && !gameMin.value) {
            gameMin.value = result.game_min;
        }
    } finally {
        checking.value = false;
    }
}

onMounted(() => {
    if (pins.value.length) {
        void recheck();
    }
});

const title = computed(() =>
    props.pack
        ? t('New version of :name', { name: props.pack.name })
        : t('Publish a mod pack'),
);

const input =
    'w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] aria-invalid:border-[#f53003] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC] dark:aria-invalid:border-[#FF4433]';
const card =
    'flex flex-col gap-5 rounded-lg bg-white p-5 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const button =
    'inline-flex rounded-md bg-[#1b1b18] px-5 py-2.5 text-sm font-medium text-white hover:bg-black disabled:opacity-60 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white';
const alert =
    'rounded-md bg-[#fff2f2] p-3 text-sm text-[#f53003] dark:bg-[#1D0002] dark:text-[#FF4433]';
</script>

<template>
    <Head :title="title" />

    <AppLayout>
        <div class="mx-auto max-w-3xl">
            <PublishTabs v-if="!pack" current="pack" />
            <a
                v-else
                :href="show(pack.id).url"
                :class="['text-sm hover:underline', muted]"
                >← {{ pack.name }}</a
            >
            <h1 class="text-2xl font-semibold tracking-tight">{{ title }}</h1>
            <p :class="['mt-1 mb-6 text-sm', muted]">
                {{
                    $t(
                        'A pack is a set of listed mods at versions that work together; Borea installs it in one go. This opens a pull request from your GitHub account that adds it to the',
                    )
                }}
                <a
                    href="https://github.com/KSAModding/content-index/tree/main/packs"
                    target="_blank"
                    rel="noopener noreferrer"
                    class="underline underline-offset-4"
                    >{{ $t('KSA content index') }}</a
                >.
                {{
                    pack
                        ? $t(
                              'It merges by itself, because your account owns the pack.',
                          )
                        : $t(
                              'Your account becomes the pack’s owner, so its later versions merge by themselves.',
                          )
                }}
            </p>

            <section v-if="!canOpenPullRequests" :class="card">
                <div>
                    <h2 class="font-semibold">{{ $t('Connect GitHub') }}</h2>
                    <p :class="['mt-1 text-sm', muted]">
                        {{
                            $t(
                                'To open the pull request for you, KSA Mods needs permission to fork the index and open pull requests from your account. GitHub calls this access to your public repositories. You are only asked once.',
                            )
                        }}
                    </p>
                </div>
                <div>
                    <a :href="pullRequests().url" :class="button">
                        {{ $t('Connect GitHub') }}
                    </a>
                </div>
            </section>

            <Form
                v-else
                v-bind="store.form()"
                v-slot="{ errors: fieldErrors, processing }"
                class="flex flex-col gap-6"
            >
                <p v-if="fieldErrors.pull_request" role="alert" :class="alert">
                    {{ fieldErrors.pull_request }}
                </p>
                <input v-if="pack" type="hidden" name="of" :value="pack.id" />

                <section :class="card">
                    <div>
                        <h2 class="font-semibold">{{ $t('Mods') }}</h2>
                        <p :class="['mt-1 text-sm', muted]">
                            {{
                                $t(
                                    'Each starts at its newest stable release. The mods they need are added for you, at versions inside every bound.',
                                )
                            }}
                        </p>
                    </div>

                    <ul
                        v-if="pins.length"
                        class="divide-y divide-[#19140014] dark:divide-[#3E3E3A]/60"
                    >
                        <li
                            v-for="(pin, index) in pins"
                            :key="pin.id"
                            class="flex flex-wrap items-center gap-3 py-3 first:pt-0 last:pb-0"
                        >
                            <ModIcon
                                :name="
                                    byId.get(pin.id.toLowerCase())?.name ??
                                    pin.id
                                "
                                :src="
                                    byId.get(pin.id.toLowerCase())?.icon ?? null
                                "
                                class="size-9 shrink-0 rounded-lg"
                            />
                            <div class="min-w-0 flex-1">
                                <p class="font-medium">
                                    {{
                                        byId.get(pin.id.toLowerCase())?.name ??
                                        pin.id
                                    }}
                                </p>
                                <p v-if="pin.added" :class="['text-xs', muted]">
                                    {{ $t('Added as a dependency') }}
                                </p>
                            </div>
                            <label class="sr-only" :for="`version-${index}`">{{
                                $t('Version')
                            }}</label>
                            <select
                                :id="`version-${index}`"
                                v-model="pin.version"
                                :class="[input, 'w-auto py-1.5']"
                                @change="recheck"
                            >
                                <option
                                    v-for="release in byId.get(
                                        pin.id.toLowerCase(),
                                    )?.releases ?? [
                                        { version: pin.version, status: '' },
                                    ]"
                                    :key="release.version"
                                    :value="release.version"
                                >
                                    {{ release.version
                                    }}<template
                                        v-if="
                                            release.status &&
                                            release.status !== 'stable'
                                        "
                                    >
                                        ({{ release.status }})</template
                                    >
                                </option>
                            </select>
                            <button
                                type="button"
                                class="text-sm text-[#f53003] hover:underline dark:text-[#FF4433]"
                                @click="remove(index)"
                            >
                                {{ $t('Remove') }}
                            </button>
                            <input
                                type="hidden"
                                :name="`mods[${index}][id]`"
                                :value="pin.id"
                            />
                            <input
                                type="hidden"
                                :name="`mods[${index}][version]`"
                                :value="pin.version"
                            />
                        </li>
                    </ul>

                    <div class="flex gap-2">
                        <label class="sr-only" for="add-mod">{{
                            $t('Add a mod')
                        }}</label>
                        <select id="add-mod" v-model="choice" :class="input">
                            <option value="">{{ $t('Add a mod…') }}</option>
                            <option
                                v-for="mod in offered"
                                :key="mod.id"
                                :value="mod.id"
                            >
                                {{ mod.name }}
                            </option>
                        </select>
                        <button
                            type="button"
                            :disabled="!choice"
                            :class="[button, 'py-2']"
                            @click="add"
                        >
                            {{ $t('Add') }}
                        </button>
                    </div>

                    <p v-if="checking" :class="['text-sm', muted]">
                        {{ $t('Checking…') }}
                    </p>
                    <ul
                        v-if="errors.length || fieldErrors.mods"
                        role="alert"
                        :class="[alert, 'flex flex-col gap-1']"
                    >
                        <li v-for="error in errors" :key="error">
                            {{ error }}
                        </li>
                        <li v-if="fieldErrors.mods && !errors.length">
                            {{ fieldErrors.mods }}
                        </li>
                    </ul>
                    <ul
                        v-if="notes.length"
                        :class="['flex flex-col gap-1 text-sm', muted]"
                    >
                        <li v-for="note in notes" :key="note">{{ note }}</li>
                    </ul>
                </section>

                <section :class="card">
                    <h2 class="font-semibold">{{ $t('About your pack') }}</h2>
                    <div class="grid gap-5 sm:grid-cols-2">
                        <FormField
                            :label="$t('Id')"
                            for="id"
                            required
                            :hint="
                                pack
                                    ? $t('An id cannot change.')
                                    : $t(
                                          'Such as flight-planning-pack. It cannot change later.',
                                      )
                            "
                            :error="fieldErrors.id"
                        >
                            <!-- Disabled for a new version, so it is not sent: "of" names the pack. -->
                            <input
                                id="id"
                                name="id"
                                required
                                :value="pack?.id"
                                :disabled="!!pack"
                                :aria-invalid="!!fieldErrors.id"
                                :class="[input, 'disabled:opacity-60']"
                            />
                        </FormField>
                        <FormField
                            :label="$t('Version')"
                            for="version"
                            required
                            :hint="
                                pack
                                    ? $t('Higher than :version.', {
                                          version: pack.previous,
                                      })
                                    : $t('Such as 1.0.0.')
                            "
                            :error="fieldErrors.version"
                        >
                            <input
                                id="version"
                                name="version"
                                required
                                :value="pack?.version ?? '1.0.0'"
                                :aria-invalid="!!fieldErrors.version"
                                :class="input"
                            />
                        </FormField>
                    </div>
                    <FormField
                        :label="$t('Name')"
                        for="name"
                        required
                        :error="fieldErrors.name"
                    >
                        <input
                            id="name"
                            name="name"
                            required
                            :value="pack?.name"
                            :aria-invalid="!!fieldErrors.name"
                            :class="input"
                        />
                    </FormField>
                    <FormField
                        :label="$t('Authors')"
                        for="authors"
                        required
                        :hint="$t('Display names, separated by commas.')"
                        :error="fieldErrors.authors"
                    >
                        <input
                            id="authors"
                            name="authors"
                            required
                            :value="pack?.authors"
                            :aria-invalid="!!fieldErrors.authors"
                            :class="input"
                        />
                    </FormField>
                    <FormField
                        :label="$t('Summary')"
                        for="abstract"
                        required
                        :hint="$t('One sentence that shows in the pack list.')"
                        :error="fieldErrors.abstract"
                    >
                        <input
                            id="abstract"
                            name="abstract"
                            required
                            maxlength="300"
                            :value="pack?.abstract"
                            :aria-invalid="!!fieldErrors.abstract"
                            :class="input"
                        />
                    </FormField>
                    <FormField
                        :label="$t('Description')"
                        for="description"
                        :hint="$t('Markdown.')"
                        :error="fieldErrors.description"
                    >
                        <textarea
                            id="description"
                            name="description"
                            rows="8"
                            :value="pack?.description"
                            :aria-invalid="!!fieldErrors.description"
                            :class="[input, 'font-mono']"
                        />
                    </FormField>
                    <FormField
                        v-if="pack"
                        :label="$t('What changed')"
                        for="changelog"
                        :hint="$t('Markdown. Shown with this version.')"
                        :error="fieldErrors.changelog"
                    >
                        <textarea
                            id="changelog"
                            name="changelog"
                            rows="4"
                            :aria-invalid="!!fieldErrors.changelog"
                            :class="[input, 'font-mono']"
                        />
                    </FormField>
                    <div class="grid gap-5 sm:grid-cols-2">
                        <FormField
                            :label="$t('License')"
                            for="license"
                            required
                            :hint="$t('An SPDX expression, such as MIT.')"
                            :error="fieldErrors.license"
                        >
                            <input
                                id="license"
                                name="license"
                                required
                                :value="pack?.license ?? 'MIT'"
                                :aria-invalid="!!fieldErrors.license"
                                :class="input"
                            />
                        </FormField>
                        <FormField
                            :label="$t('Oldest game build')"
                            for="game_min"
                            :hint="
                                $t(
                                    'Set to what the pinned mods need, at least.',
                                )
                            "
                            :error="fieldErrors.game_min"
                        >
                            <select
                                id="game_min"
                                v-model="gameMin"
                                name="game_min"
                                :class="input"
                            >
                                <option value="">{{ $t('Not set') }}</option>
                                <option
                                    v-for="version in gameVersions"
                                    :key="version"
                                    :value="version"
                                >
                                    {{ version }}
                                </option>
                            </select>
                        </FormField>
                    </div>
                    <fieldset>
                        <legend class="mb-2 text-sm font-medium">
                            {{ $t('Tags') }}
                            <span :class="['font-normal', muted]">{{
                                $t('(optional)')
                            }}</span>
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
                                    :checked="pack?.tags.includes(tag.tag)"
                                />
                                {{ tag.name }}
                            </label>
                        </div>
                    </fieldset>
                </section>

                <section :class="card">
                    <h2 class="font-semibold">{{ $t('Links and icon') }}</h2>
                    <FormField
                        :label="$t('Forum thread')"
                        for="forums"
                        required
                        :hint="
                            $t(
                                'Your pack’s thread on forums.ahwoo.com. The index needs it.',
                            )
                        "
                        :error="fieldErrors.forums"
                    >
                        <input
                            id="forums"
                            name="forums"
                            type="url"
                            required
                            :value="pack?.forums"
                            :aria-invalid="!!fieldErrors.forums"
                            :class="input"
                        />
                    </FormField>
                    <FormField
                        :label="$t('Repository')"
                        for="repository"
                        :error="fieldErrors.repository"
                    >
                        <input
                            id="repository"
                            name="repository"
                            type="url"
                            :value="pack?.repository"
                            :aria-invalid="!!fieldErrors.repository"
                            :class="input"
                        />
                    </FormField>
                    <FormField
                        :label="$t('Icon')"
                        for="icon"
                        :hint="
                            pack?.icon
                                ? $t(
                                      'Leave empty to keep the current icon. A new one is cut to its center square.',
                                  )
                                : $t(
                                      'PNG, JPEG or WebP, at least 256 pixels; it is cut to its center square and hosted here.',
                                  )
                        "
                        :error="fieldErrors.icon"
                    >
                        <div class="flex items-center gap-3">
                            <img
                                v-if="pack?.icon"
                                :src="pack.icon"
                                alt=""
                                class="size-12 rounded-lg object-cover"
                            />
                            <input
                                id="icon"
                                name="icon"
                                type="file"
                                accept="image/png,image/jpeg,image/webp"
                                :class="input"
                            />
                        </div>
                    </FormField>
                </section>

                <div class="flex items-center gap-4">
                    <button
                        type="submit"
                        :disabled="
                            processing ||
                            checking ||
                            !pins.length ||
                            errors.length > 0
                        "
                        :class="button"
                    >
                        {{
                            processing
                                ? $t('Opening pull request…')
                                : $t('Open pull request')
                        }}
                    </button>
                    <p v-if="processing" :class="['text-sm', muted]">
                        {{
                            $t(
                                'This takes a few seconds while GitHub sets up your fork.',
                            )
                        }}
                    </p>
                </div>
            </Form>
        </div>
    </AppLayout>
</template>
