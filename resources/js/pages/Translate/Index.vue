<script setup lang="ts">
import { Head, Link, router, useForm, usePage } from '@inertiajs/vue3';
import FormField from '@/components/FormField.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { t } from '@/lib/i18n';
import { login } from '@/routes';
import {
    approve,
    destroyLanguage,
    request as requestLanguage,
    show,
} from '@/routes/translate';

defineProps<{
    languages: {
        code: string;
        name: string;
        native_name: string;
        active: boolean;
        progress: number;
    }[];
    texts: number;
    threshold: number;
    canApprove: boolean;
}>();

const page = usePage();
const form = useForm({ code: '', name: '', native_name: '' });

function remove(code: string, name: string) {
    if (
        confirm(t('Remove :name and every suggestion made for it?', { name }))
    ) {
        router.delete(destroyLanguage(code).url);
    }
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'rounded-lg bg-white p-5 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
const input =
    'w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] aria-invalid:border-[#f53003] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC] dark:aria-invalid:border-[#FF4433]';
</script>

<template>
    <Head :title="$t('Translate ksamods.gg')" />

    <AppLayout>
        <div class="mx-auto flex max-w-3xl flex-col gap-8">
            <div>
                <h1 class="text-2xl font-semibold tracking-tight">
                    {{ $t('Translate ksamods.gg') }}
                </h1>
                <p :class="['mt-1 text-sm', muted]">
                    {{
                        $t(
                            'The site is translated by its players. Suggest a translation for any of its :count texts, and vote on the suggestions of others: the best one shows once it has :threshold points, its author counting as one.',
                            { count: texts, threshold },
                        )
                    }}
                </p>
            </div>

            <p
                v-if="page.flash.status"
                role="status"
                class="rounded-md bg-accent-wash p-3 text-sm text-accent"
            >
                {{ page.flash.status }}
            </p>

            <section>
                <h2 class="mb-3 font-semibold">{{ $t('Languages') }}</h2>
                <ul
                    v-if="languages.length"
                    class="divide-y divide-[#19140014] rounded-lg bg-white shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:divide-[#3E3E3A]/60 dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]"
                >
                    <li
                        v-for="language in languages"
                        :key="language.code"
                        class="flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3"
                    >
                        <div class="min-w-0 flex-1">
                            <Link
                                :href="show(language.code)"
                                class="font-medium hover:underline"
                                >{{ language.native_name }}</Link
                            >
                            <span :class="['ml-2 text-xs', muted]"
                                >{{ language.name }} · {{ language.code }}</span
                            >
                            <span
                                v-if="!language.active"
                                class="ml-2 rounded-full bg-[#f4f3ef] px-2 py-0.5 text-xs dark:bg-[#232321]"
                                >{{ $t('Requested') }}</span
                            >
                        </div>
                        <div class="flex w-32 items-center gap-2">
                            <div
                                class="h-1.5 flex-1 overflow-hidden rounded-full bg-[#f4f3ef] dark:bg-[#232321]"
                                role="progressbar"
                                :aria-valuenow="language.progress"
                                aria-valuemin="0"
                                aria-valuemax="100"
                                :aria-label="
                                    $t(':name translated', {
                                        name: language.name,
                                    })
                                "
                            >
                                <div
                                    class="h-full rounded-full bg-accent"
                                    :style="{ width: `${language.progress}%` }"
                                />
                            </div>
                            <span
                                :class="[
                                    'w-9 text-right text-xs tabular-nums',
                                    muted,
                                ]"
                                >{{ language.progress }}%</span
                            >
                        </div>
                        <span v-if="canApprove" class="flex gap-3 text-xs">
                            <Link
                                v-if="!language.active"
                                :href="approve(language.code)"
                                as="button"
                                class="underline underline-offset-4"
                                >{{ $t('Turn on') }}</Link
                            >
                            <button
                                type="button"
                                class="text-[#f53003] hover:underline dark:text-[#FF4433]"
                                @click="remove(language.code, language.name)"
                            >
                                {{ $t('Remove') }}
                            </button>
                        </span>
                    </li>
                </ul>
                <p
                    v-else
                    :class="[
                        'rounded-lg border border-dashed border-[#19140035] p-8 text-center text-sm dark:border-[#3E3E3A]',
                        muted,
                    ]"
                >
                    {{ $t('No languages yet. Request the first one below.') }}
                </p>
            </section>

            <section :class="card">
                <h2 class="font-semibold">{{ $t('Request a language') }}</h2>
                <p :class="['mt-1 mb-4 text-sm', muted]">
                    {{
                        $t(
                            'You can translate it straight away; an admin turns it on for everyone.',
                        )
                    }}
                </p>
                <form
                    v-if="page.props.auth.user"
                    class="grid gap-4 sm:grid-cols-3"
                    @submit.prevent="form.post(requestLanguage().url)"
                >
                    <FormField
                        :label="$t('Code')"
                        for="code"
                        required
                        :error="form.errors.code"
                        :hint="$t('Such as de or pt-BR.')"
                    >
                        <input
                            id="code"
                            v-model="form.code"
                            required
                            maxlength="16"
                            :class="[input, 'font-mono']"
                        />
                    </FormField>
                    <FormField
                        :label="$t('Name in English')"
                        for="name"
                        required
                        :error="form.errors.name"
                    >
                        <input
                            id="name"
                            v-model="form.name"
                            required
                            maxlength="60"
                            :class="input"
                        />
                    </FormField>
                    <FormField
                        :label="$t('Its own name')"
                        for="native_name"
                        required
                        :error="form.errors.native_name"
                        :hint="$t('Such as Deutsch.')"
                    >
                        <input
                            id="native_name"
                            v-model="form.native_name"
                            required
                            maxlength="60"
                            :class="input"
                        />
                    </FormField>
                    <div class="sm:col-span-3">
                        <button
                            type="submit"
                            :disabled="form.processing"
                            class="rounded-md bg-[#1b1b18] px-5 py-2.5 text-sm font-medium text-white hover:bg-black disabled:opacity-50 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                        >
                            {{ $t('Request it') }}
                        </button>
                    </div>
                </form>
                <p v-else class="text-sm">
                    <Link
                        :href="login()"
                        class="underline underline-offset-4"
                        >{{ $t('Log in') }}</Link
                    >
                    {{ $t('to request a language or translate.') }}
                </p>
            </section>
        </div>
    </AppLayout>
</template>
