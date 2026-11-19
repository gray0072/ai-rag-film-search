import { AlertCircle } from "lucide-react";
import { useId, useState, type SubmitEvent } from "react";
import { useTranslation } from "react-i18next";
import { api } from "@/api/client";
import { problemStatus, type ApiProblem } from "@/api/problem";
import type { components } from "@/api/schema";
import { ErrorState } from "@/components/StatusMessages";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { AdminSection } from "@/features/admin/AdminLayout";
import { SelectField } from "@/features/filters/FiltersPanel";

type AdminAiProfiles = components["schemas"]["AdminAiProfiles"];

/** AI profiles (I22): what is configured and active, and one test call to a chat profile with tokens and cost. */
export function AdminAiPage() {
  const { t } = useTranslation();
  const profiles = api.useQuery("get", "/api/admin/ai/profiles");

  if (profiles.isPending) {
    return (
      <div role="status" aria-label={t("common.loading")}>
        <Skeleton className="h-40 w-full" />
      </div>
    );
  }
  if (profiles.isError) {
    return <ErrorState onRetry={() => void profiles.refetch()} />;
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="max-w-3xl text-sm text-muted-foreground">{t("admin.ai.description")}</p>
      <div className="grid gap-4 lg:grid-cols-2">
        <ProfileTables profiles={profiles.data} />
        <TryProfile profiles={profiles.data} />
      </div>
    </div>
  );
}

function ProfileTables({ profiles }: { profiles: AdminAiProfiles }) {
  const { t } = useTranslation();
  const yesNo = (value: boolean) => (value ? t("admin.yes") : t("admin.no"));
  const head = "py-2 pr-3 font-medium";
  const cell = "py-2 pr-3";

  return (
    <div className="flex flex-col gap-4">
      <AdminSection title={t("admin.ai.chatProfiles")}>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-xs text-muted-foreground">
              <tr>
                <th className={head}>{t("admin.ai.columns.name")}</th>
                <th className={head}>{t("admin.ai.columns.model")}</th>
                <th className={head}>{t("admin.ai.columns.apiKey")}</th>
                <th className={head}>{t("admin.ai.columns.tools")}</th>
              </tr>
            </thead>
            <tbody>
              {profiles.chatProfiles.map((profile) => (
                <tr key={profile.name} className="border-t align-top">
                  <td className={cell}>
                    <span className="mr-1">{profile.name}</span>
                    {profile.name === profiles.activeChatProfile && (
                      <Badge variant="secondary">{t("admin.ai.active")}</Badge>
                    )}
                    {profile.name === profiles.fallbackChatProfile && (
                      <Badge variant="outline">{t("admin.ai.fallback")}</Badge>
                    )}
                  </td>
                  <td className={`${cell} break-all`}>{profile.model}</td>
                  <td className={cell}>{yesNo(profile.hasApiKey)}</td>
                  <td className={cell}>{yesNo(profile.supportsTools)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </AdminSection>
      <AdminSection title={t("admin.ai.embeddingProfiles")}>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-xs text-muted-foreground">
              <tr>
                <th className={head}>{t("admin.ai.columns.name")}</th>
                <th className={head}>{t("admin.ai.columns.model")}</th>
                <th className={head}>{t("admin.ai.columns.dimensions")}</th>
                <th className={head}>{t("admin.ai.columns.apiKey")}</th>
              </tr>
            </thead>
            <tbody>
              {profiles.embeddingProfiles.map((profile) => (
                <tr key={profile.name} className="border-t align-top">
                  <td className={cell}>
                    <span className="mr-1">{profile.name}</span>
                    {profile.name === profiles.activeEmbeddingProfile && (
                      <Badge variant="secondary">{t("admin.ai.active")}</Badge>
                    )}
                  </td>
                  <td className={`${cell} break-all`}>{profile.model}</td>
                  <td className={`${cell} tabular-nums`}>{profile.dimensions}</td>
                  <td className={cell}>{yesNo(profile.hasApiKey)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </AdminSection>
    </div>
  );
}

function TryProfile({ profiles }: { profiles: AdminAiProfiles }) {
  const { t, i18n } = useTranslation();
  const id = useId();
  const [profile, setProfile] = useState(profiles.activeChatProfile);
  const [message, setMessage] = useState(() => t("admin.ai.test.defaultMessage"));
  const chat = api.useMutation("post", "/api/admin/ai/chat");
  const selected = profiles.chatProfiles.find((p) => p.name === profile);
  const usd = new Intl.NumberFormat(i18n.language, {
    style: "currency",
    currency: "USD",
    maximumSignificantDigits: 3,
  });
  const number = new Intl.NumberFormat(i18n.language);

  const onSubmit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    chat.mutate({ body: { profile, message } });
  };

  const errorDetail = problemMessage(chat.error);

  return (
    <AdminSection title={t("admin.ai.test.title")} description={t("admin.ai.test.description")}>
      <form onSubmit={onSubmit} className="flex flex-col gap-3">
        <SelectField label={t("admin.ai.test.profile")} value={profile} onChange={setProfile}>
          {profiles.chatProfiles.map((p) => (
            <option key={p.name} value={p.name}>
              {p.name} ({p.model})
            </option>
          ))}
        </SelectField>
        {selected && !selected.hasApiKey && (
          <p className="text-sm text-muted-foreground">{t("admin.ai.test.noKey")}</p>
        )}
        <div className="flex flex-col gap-1">
          <Label htmlFor={`${id}-message`} className="font-medium">
            {t("admin.ai.test.message")}
          </Label>
          <Textarea
            id={`${id}-message`}
            value={message}
            rows={3}
            maxLength={4000}
            required
            onChange={(event) => {
              setMessage(event.target.value);
            }}
          />
        </div>
        <div>
          <Button type="submit" disabled={chat.isPending || message.trim().length === 0}>
            {chat.isPending ? t("admin.ai.test.sending") : t("admin.ai.test.submit")}
          </Button>
        </div>
      </form>

      {chat.data && (
        <div className="flex flex-col gap-2 rounded-md bg-muted p-3 text-sm">
          <span className="font-medium">
            {t("admin.ai.test.answer", { model: chat.data.model })}
          </span>
          <p className="whitespace-pre-wrap">{chat.data.text}</p>
          <span className="text-xs text-muted-foreground">
            {t("admin.ai.test.usage", {
              input: number.format(chat.data.inputTokens),
              output: number.format(chat.data.outputTokens),
            })}
            {" · "}
            {chat.data.estimatedCostUsd === null
              ? t("admin.ai.test.noCost")
              : t("admin.ai.test.cost", { cost: usd.format(chat.data.estimatedCostUsd) })}
          </span>
        </div>
      )}
      {chat.isError &&
        (problemStatus(chat.error) === 503 ? (
          <Alert variant="destructive">
            <AlertCircle aria-hidden />
            <AlertTitle>{t("admin.ai.test.unavailable")}</AlertTitle>
            {errorDetail && (
              <AlertDescription>
                <p className="break-words">{errorDetail}</p>
              </AlertDescription>
            )}
          </Alert>
        ) : (
          <ErrorState message={errorDetail || undefined} />
        ))}
    </AdminSection>
  );
}

/** The detail of a Problem Details error, or its first validation message (e.g. a profile without a key). */
function problemMessage(error: unknown): string {
  if (typeof error !== "object" || error === null) {
    return "";
  }
  const problem = error as ApiProblem;
  return problem.detail ?? Object.values(problem.errors ?? {}).flat()[0] ?? "";
}
