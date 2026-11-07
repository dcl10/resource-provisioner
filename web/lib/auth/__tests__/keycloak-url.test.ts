import { afterEach, describe, expect, it, vi } from "vitest";

import { keycloakFetch, toInternalKeycloakUrl } from "../keycloak-url";

const env = {
  AUTH_KEYCLOAK_ISSUER: "http://localhost:8080/realms/resource-provisioner",
  AUTH_KEYCLOAK_INTERNAL_ISSUER:
    "http://keycloak:8080/realms/resource-provisioner",
};

describe("toInternalKeycloakUrl", () => {
  it("swaps the public origin for the internal one", () => {
    expect(
      toInternalKeycloakUrl(
        "http://localhost:8080/realms/resource-provisioner/.well-known/openid-configuration",
        env,
      ),
    ).toBe(
      "http://keycloak:8080/realms/resource-provisioner/.well-known/openid-configuration",
    );
  });

  it("keeps the path and query", () => {
    expect(
      toInternalKeycloakUrl(
        new URL(
          "http://localhost:8080/realms/r/protocol/openid-connect/token?x=1",
        ),
        env,
      ),
    ).toBe("http://keycloak:8080/realms/r/protocol/openid-connect/token?x=1");
  });

  it("leaves other origins alone", () => {
    expect(toInternalKeycloakUrl("http://api:8080/api/me", env)).toBe(
      "http://api:8080/api/me",
    );
  });

  it("changes nothing without an internal issuer", () => {
    expect(
      toInternalKeycloakUrl("http://localhost:8080/realms/r/x", {
        AUTH_KEYCLOAK_ISSUER: env.AUTH_KEYCLOAK_ISSUER,
      }),
    ).toBe("http://localhost:8080/realms/r/x");
  });
});

describe("keycloakFetch", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.unstubAllGlobals();
  });

  it("sends server-side Keycloak calls to the internal address", async () => {
    vi.stubEnv("AUTH_KEYCLOAK_ISSUER", env.AUTH_KEYCLOAK_ISSUER);
    vi.stubEnv(
      "AUTH_KEYCLOAK_INTERNAL_ISSUER",
      env.AUTH_KEYCLOAK_INTERNAL_ISSUER,
    );
    const fetchMock = vi.fn(async () => new Response("{}"));
    vi.stubGlobal("fetch", fetchMock);

    await keycloakFetch(
      new Request(
        "http://localhost:8080/realms/resource-provisioner/protocol/openid-connect/token",
        {
          method: "POST",
          body: "grant_type=authorization_code",
        },
      ),
    );

    const [url, init] = fetchMock.mock.calls[0] as unknown as [
      string,
      RequestInit,
    ];
    expect(url).toBe(
      "http://keycloak:8080/realms/resource-provisioner/protocol/openid-connect/token",
    );
    expect(init.method).toBe("POST");
    expect(new TextDecoder().decode(init.body as ArrayBuffer)).toBe(
      "grant_type=authorization_code",
    );
  });
});
