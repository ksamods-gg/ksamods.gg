import { t } from '@/lib/i18n';
import type { NotificationData } from '@/types';

// What happened, in one line, the same in the header's toast and on the notifications page.
export function notificationText(data: NotificationData): string {
    switch (data.kind) {
        case 'reply':
            return t(':actor replied to your comment on :mod', {
                actor: data.actor.name,
                mod: data.mod.name,
            });
        case 'review_reply':
            return t(':actor replied to your review of :mod', {
                actor: data.actor.name,
                mod: data.mod.name,
            });
        case 'mention':
            return t(':actor mentioned you on :mod', {
                actor: data.actor.name,
                mod: data.mod.name,
            });
        case 'release':
            return t(':mod :version is out', {
                mod: data.subject.name,
                version: data.version ?? '',
            });
        case 'share_merged':
            return t(':mod is in the index', { mod: data.subject.name });
        case 'share_closed':
            return t(
                'The listing pull request of :mod was closed without merging',
                { mod: data.subject.name },
            );
        case 'share_mirrored':
            return t(':mod :version has its archive.org copy', {
                mod: data.subject.name,
                version: data.version ?? '',
            });
        case 'share_taken_down':
            return t(':mod :version was taken down', {
                mod: data.subject.name,
                version: data.version ?? '',
            });
    }
}

// Who or what the notification shows a picture of.
export function notificationPicture(data: NotificationData): {
    name: string;
    src: string | null;
} {
    return 'actor' in data
        ? { name: data.actor.name, src: data.actor.avatar }
        : { name: data.subject.name, src: data.subject.icon };
}

// The comment excerpt, for notifications about a comment.
export const notificationExcerpt = (data: NotificationData): string | null =>
    'excerpt' in data ? data.excerpt : null;

// Each user's private channel, which Laravel's broadcast notifications use.
export const notificationChannel = (userId: number) =>
    `App.Models.User.${userId}`;
