<script setup lang="ts">
import { Head, Link, router, useForm, usePage } from '@inertiajs/vue3';
import { reactive, ref } from 'vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { t } from '@/lib/i18n';
import { login } from '@/routes';
import { destroy, index, show, suggest, vote } from '@/routes/translate';

type Suggestion = {
    id: number;
    text: string;
    score: number;
    author: string | null;
    mine: boolean;
    vote: number;
    canDelete: boolean;
};

const props = defineProps<{
    language: {
        code: string;
        name: string;
        native_name: string;
        active: boolean;
    };
    search: string;
    show: 'untranslated' | 'all';
    page: number;
    pages: number;
    total: number;
    threshold: number;
    texts: {
        source: string;
        live: string | null;
        suggestions: Suggestion[];
    }[];
}>();

const page = usePage();
const query = ref(props.search);

function filter(params: Record<string, string | number | undefined>) {
    router.get(
        show(props.language.code).url,
        {
            q: query.value || undefined,
            show: props.show === 'all' ? 'all' : undefined,
            ...params,
        },
        { preserveState: true, preserveScroll: params.page === undefined },
    );
}

// One draft per text, so typing in one box leaves the others alone.
const drafts = reactive<Record<string, string>>({});
const form = useForm({ source: '', text: '' });
const sending = ref<string | null>(null);

function send(source: string) {
    form.source = source;
    form.text = drafts[source] ?? '';
    sending.value = source;
    form.post(suggest(props.language.code).url, {
        preserveScroll: true,
        onSuccess: () => (drafts[source] = ''),
    });
}

function castVote(suggestion: Suggestion, value: number) {
    router.post(
        vote(suggestion.id).url,
        { value: suggestion.vote === value ? 0 : value },
        { preserveScroll: true },
    );
}

function remove(suggestion: Suggestion) {
    if (confirm(t('Delete this suggestion?'))) {
        router.delete(destroy(suggestion.id).url, { preserveScroll: true });
    }
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const control =
    'rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC]';
const card =
    'rounded-lg bg-white p-4 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
const voteButton =
    'flex size-7 items-center justify-center rounded-md border border-[#19140035] text-xs hover:border-[#1915014a] disabled:opacity-40 aria-pressed:border-accent aria-pressed:bg-accent-wash aria-pressed:text-accent dark:border-[#3E3E3A] dark:hover:border-[#62605b]';
</script>

<template>
    <Head
        :title="$t('Translate into :language', { language: language.name })"
    />

    <AppLayout>
        <div class="mx-auto flex max-w-4xl flex-col gap-6">
            <div>
                <Link
                    :href="index()"
                    :class="['text-sm hover:underline', muted]"
                >
                    ← {{ $t('All languages') }}
                </Link>
                <h1 class="mt-3 text-2xl font-semibold tracking-tight">
                    {{ language.native_name }}
                    <span :class="['text-lg font-normal', muted]">{{
                        language.name
                    }}</span>
                </h1>
                <p :class="['mt-1 text-sm', muted]">
                    {{
                        $t(
                            'Suggest a translation, or vote for the best one. A suggestion shows on the site once it has :threshold points. Keep words like :name as they are; the site fills them in.',
                            { threshold },
                        )
                    }}
                    <template v-if="!language.active">
                        {{
                            $t(
                                'This language is not on yet; an admin turns it on.',
                            )
                        }}
                    </template>
                </p>
            </div>

            <div class="flex flex-wrap items-center gap-3">
                <form class="flex-1" role="search" @submit.prevent="filter({})">
                    <label class="block">
                        <span class="sr-only">{{ $t('Search texts') }}</span>
                        <input
                            v-model="query"
                            type="search"
                            :placeholder="$t('Search texts')"
                            :class="[control, 'w-full']"
                        />
                    </label>
                </form>
                <div
                    class="flex rounded-md border border-[#19140035] p-0.5 text-sm dark:border-[#3E3E3A]"
                    role="group"
                    :aria-label="$t('Which texts')"
                >
                    <button
                        v-for="option in ['untranslated', 'all'] as const"
                        :key="option"
                        type="button"
                        :aria-pressed="props.show === option"
                        class="rounded px-3 py-1 aria-pressed:bg-[#f4f3ef] aria-pressed:font-medium dark:aria-pressed:bg-[#232321]"
                        @click="
                            filter({
                                show: option === 'all' ? 'all' : undefined,
                                page: undefined,
                            })
                        "
                    >
                        {{ option === 'all' ? $t('All') : $t('Untranslated') }}
                    </button>
                </div>
                <p :class="['text-sm', muted]">
                    {{ $t(':count texts', { count: total }) }}
                </p>
            </div>

            <p v-if="!page.props.auth.user" :class="['text-sm', muted]">
                <Link :href="login()" class="underline underline-offset-4">{{
                    $t('Log in')
                }}</Link>
                {{ $t('to suggest translations and vote.') }}
            </p>

            <ul v-if="texts.length" class="flex flex-col gap-3">
                <li v-for="item in texts" :key="item.source" :class="card">
                    <p
                        class="text-sm font-medium break-words whitespace-pre-line"
                    >
                        {{ item.source }}
                    </p>
                    <p
                        v-if="item.live"
                        class="mt-1 text-sm break-words whitespace-pre-line text-accent"
                    >
                        {{ item.live }}
                    </p>

                    <ul
                        v-if="item.suggestions.length"
                        class="mt-3 flex flex-col gap-2 border-t border-[#19140014] pt-3 dark:border-[#3E3E3A]/60"
                    >
                        <li
                            v-for="suggestion in item.suggestions"
                            :key="suggestion.id"
                            class="flex items-start gap-3"
                        >
                            <div class="flex shrink-0 items-center gap-1">
                                <button
                                    type="button"
                                    :class="voteButton"
                                    :aria-pressed="suggestion.vote === 1"
                                    :aria-label="$t('Vote up')"
                                    :disabled="
                                        !page.props.auth.user || suggestion.mine
                                    "
                                    @click="castVote(suggestion, 1)"
                                >
                                    ▲
                                </button>
                                <span
                                    class="w-6 text-center text-sm tabular-nums"
                                    >{{ suggestion.score }}</span
                                >
                                <button
                                    type="button"
                                    :class="voteButton"
                                    :aria-pressed="suggestion.vote === -1"
                                    :aria-label="$t('Vote down')"
                                    :disabled="
                                        !page.props.auth.user || suggestion.mine
                                    "
                                    @click="castVote(suggestion, -1)"
                                >
                                    ▼
                                </button>
                            </div>
                            <div class="min-w-0 flex-1">
                                <p
                                    class="text-sm break-words whitespace-pre-line"
                                >
                                    {{ suggestion.text }}
                                </p>
                                <p :class="['text-xs', muted]">
                                    {{
                                        suggestion.author
                                            ? $t('by :name', {
                                                  name: suggestion.author,
                                              })
                                            : $t('by a deleted account')
                                    }}
                                    <button
                                        v-if="suggestion.canDelete"
                                        type="button"
                                        class="ml-2 text-[#f53003] hover:underline dark:text-[#FF4433]"
                                        @click="remove(suggestion)"
                                    >
                                        {{ $t('Delete') }}
                                    </button>
                                </p>
                            </div>
                        </li>
                    </ul>

                    <form
                        v-if="page.props.auth.user"
                        class="mt-3 flex flex-col gap-2 sm:flex-row sm:items-start"
                        @submit.prevent="send(item.source)"
                    >
                        <label class="flex-1">
                            <span class="sr-only">{{
                                $t('Your translation')
                            }}</span>
                            <textarea
                                v-model="drafts[item.source]"
                                rows="1"
                                :placeholder="$t('Your translation')"
                                :class="[control, 'w-full resize-y']"
                            />
                        </label>
                        <button
                            type="submit"
                            :disabled="
                                !drafts[item.source]?.trim() || form.processing
                            "
                            class="rounded-md bg-[#1b1b18] px-4 py-2 text-sm font-medium text-white hover:bg-black disabled:opacity-50 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                        >
                            {{ $t('Suggest') }}
                        </button>
                    </form>
                    <p
                        v-if="sending === item.source && form.errors.text"
                        role="alert"
                        class="mt-1 text-xs text-[#f53003] dark:text-[#FF4433]"
                    >
                        {{ form.errors.text }}
                    </p>
                </li>
            </ul>
            <p
                v-else
                :class="[
                    'rounded-lg border border-dashed border-[#19140035] p-10 text-center text-sm dark:border-[#3E3E3A]',
                    muted,
                ]"
            >
                {{
                    props.show === 'untranslated' && !search
                        ? $t('Every text has a translation. Thank you!')
                        : $t('No texts match.')
                }}
            </p>

            <nav
                v-if="pages > 1"
                :aria-label="$t('Pages')"
                class="flex items-center justify-between text-sm"
            >
                <button
                    type="button"
                    :disabled="props.page <= 1"
                    class="underline underline-offset-4 disabled:no-underline disabled:opacity-40"
                    @click="filter({ page: props.page - 1 })"
                >
                    ← {{ $t('Previous') }}
                </button>
                <span :class="muted">{{
                    $t('Page :page of :pages', { page: props.page, pages })
                }}</span>
                <button
                    type="button"
                    :disabled="props.page >= pages"
                    class="underline underline-offset-4 disabled:no-underline disabled:opacity-40"
                    @click="filter({ page: props.page + 1 })"
                >
                    {{ $t('Next') }} →
                </button>
            </nav>
        </div>
    </AppLayout>
</template>
