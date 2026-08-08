# The migration runner.
#
# The SQL is baked into the image rather than bind-mounted from the repo, and that is not a
# stylistic choice. Deployment tools like Coolify run `docker compose` inside a helper container
# while talking to the host's Docker daemon, so a relative bind mount resolves to a path that
# exists in the helper but not on the host - the daemon then silently creates an empty directory
# there and the migration file is simply absent. Copying it in makes the image self-contained and
# identical everywhere it runs.

FROM postgres:17-alpine

COPY migrations/ /migrations/
COPY apply.sh /apply.sh

RUN chmod +x /apply.sh

ENTRYPOINT ["/apply.sh"]
