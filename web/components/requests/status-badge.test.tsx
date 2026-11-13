import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { StatusBadge } from "@/components/requests/status-badge";
import type { RequestStatus } from "@/lib/requests";

describe("StatusBadge", () => {
  it.each<[RequestStatus, string]>([
    ["AwaitingConfirmation", "Awaiting confirmation"],
    ["Queued", "Queued"],
    ["Applying", "Provisioning"],
    ["Succeeded", "Provisioned"],
    ["Failed", "Failed"],
    ["Destroying", "Destroying"],
  ])("labels %s as %s", (status, label) => {
    render(<StatusBadge status={status} />);

    expect(screen.getByText(label)).toHaveAttribute("data-status", status);
  });

  it.each<RequestStatus>(["Queued", "Applying", "Destroying"])(
    "shows a spinner while %s",
    (status) => {
      const { container } = render(<StatusBadge status={status} />);

      expect(container.querySelector(".animate-spin")).not.toBeNull();
    },
  );

  it.each<RequestStatus>(["AwaitingConfirmation", "Succeeded", "Failed"])(
    "shows no spinner once %s",
    (status) => {
      const { container } = render(<StatusBadge status={status} />);

      expect(container.querySelector(".animate-spin")).toBeNull();
    },
  );

  it("marks a failure as destructive", () => {
    render(<StatusBadge status="Failed" />);

    expect(screen.getByText("Failed")).toHaveClass("bg-destructive/10");
  });
});
