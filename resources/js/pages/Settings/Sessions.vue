<script setup lang="ts">
import { Head, Link, usePage } from '@inertiajs/vue3';
import SettingsLayout from '@/layouts/SettingsLayout.vue';
import { formatDate } from '@/lib/format';
import { destroy, destroyOthers } from '@/routes/settings/sessions';

defineProps<{
    supported: boolean;
    sessions: {
        id: string;
        device: string;
        ip: string | null;
        last_active: string;
        current: boolean;
    }[];
}>();

const page = usePage();

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const heading =
    'mb-4 border-b border-[#19140014] pb-2 text-xl font-semibold tracking-tight dark:border-[#3E3E3A]/60';
const button =
    'shrink-0 rounded-md border border-[#19140035] px-3 py-1.5 text-sm hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]';
</script>

<template>
    <Head :title="$t('Sessions')" />

    <SettingsLayout>
        <section>
            <h1 :class="heading">{{ $t('Sessions') }}</h1>
            <p :class="['mb-4 text-sm', muted]">
                {{
                    $t(
                        "The browsers signed in to your account. If you don't recognise one, sign it out. With a confirmed digest email, you also get an email when your account is signed in to from a new browser.",
                    )
                }}
            </p>

            <p
                v-if="page.flash.status"
                role="status"
                class="mb-4 rounded-md bg-accent-wash p-3 text-sm text-accent"
            >
                {{ page.flash.status }}
            </p>

            <p
                v-if="!supported"
                :class="[
                    'rounded-lg border border-dashed border-[#19140035] p-6 text-sm dark:border-[#3E3E3A]',
                    muted,
                ]"
            >
                {{
                    $t('The session list needs :setting.', {
                        setting: 'SESSION_DRIVER=database',
                    })
                }}
            </p>

            <template v-else>
                <ul
                    class="divide-y divide-[#19140014] rounded-lg bg-white shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:divide-[#3E3E3A]/60 dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]"
                >
                    <li
                        v-for="session in sessions"
                        :key="session.id"
                        class="flex flex-wrap items-center justify-between gap-3 px-4 py-3.5"
                    >
                        <div class="min-w-0">
                            <p class="text-sm font-medium">
                                {{ session.device }}
                                <span
                                    v-if="session.current"
                                    class="ml-1 rounded-full bg-accent-wash px-2 py-0.5 text-xs font-medium text-accent"
                                    >{{ $t('This browser') }}</span
                                >
                            </p>
                            <p :class="['text-xs', muted]">
                                <template v-if="session.ip"
                                    >{{ session.ip }} ·
                                </template>
                                {{
                                    $t('Last active :date', {
                                        date: formatDate(session.last_active),
                                    })
                                }}
                            </p>
                        </div>
                        <Link
                            v-if="!session.current"
                            :href="destroy(session.id)"
                            as="button"
                            preserve-scroll
                            :class="button"
                        >
                            {{ $t('Sign out') }}
                        </Link>
                    </li>
                </ul>

                <div
                    v-if="sessions.length > 1"
                    class="mt-4 flex flex-wrap items-center gap-3"
                >
                    <Link
                        :href="destroyOthers()"
                        as="button"
                        preserve-scroll
                        :class="button"
                    >
                        {{ $t('Sign out of all other sessions') }}
                    </Link>
                    <span :class="['text-xs', muted]">
                        {{
                            $t('Their "remember me" sign-ins stop working too.')
                        }}
                    </span>
                </div>
            </template>
        </section>
    </SettingsLayout>
</template>
