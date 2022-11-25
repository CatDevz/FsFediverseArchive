namespace MkDisplay
// Disable warnings for implicit conversions (Request.Path (pathstring => string))
#nowarn "3391"

open System.Threading.Tasks

open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging

open Google.Cloud.Functions.Framework
open Google.Cloud.Firestore

open MkDisplay.Types
open MkDisplay.Firestore
open MkDisplay.Views

type Routes =
  | Notes
  | Note of noteId: string

  static member FromParams page limit =
    match page, limit with
    | Some page, Some limit -> { page = page; limit = limit }
    | None, Some limit -> { page = 1; limit = limit }
    | Some page, None -> { page = page; limit = 10 }
    | None, None -> { page = 1; limit = 10 }

[<RequireQualifiedAccess>]
module Router =
  open Elmish.UrlParser

  let get route =
    parseUrl (oneOf [ map Note (s "notes" </> str); map Notes (s "notes" </> top) ]) route

  let query route =
    parseUrl (map (Routes.FromParams) (top <?> intParam "page" <?> intParam "limit")) route
    |> function
      | Some pagination -> pagination
      | None -> { page = 1; limit = 10 }


type Function(logger: ILogger<Function>) =

  let mutable db: FirestoreDb option = None

  interface IHttpFunction with
    /// <summary>
    /// Logic for your function goes here.
    /// </summary>
    /// <param name="context">The HTTP context, containing the request and the response.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    member _.HandleAsync context = task {
      let request = context.Request
      let response = context.Response

      match Router.get request.Path with
      | None
      | Some Notes ->
        logger.LogInformation(request.Path)
        let pagination = Router.query request.QueryString.Value

        let! db =
          db
          |> Option.map (Task.FromResult)
          |> Option.defaultWith (fun _ -> FirestoreDb.CreateAsync("misskey-automations"))

        let! notes =
          try
            NoteRecord.paginate (logger, db) pagination
          with ex ->
            logger.LogDebug($"Failed to retreive notes", ex)
            Task.FromResult(List.empty)

        response.ContentType <- "text/html;charset=utf-8"
        response.StatusCode <- 200
        return! notes |> Render.Notes pagination |> response.WriteAsync

      | Some(Note note) ->
        let! db =
          db
          |> Option.map (Task.FromResult)
          |> Option.defaultWith (fun _ -> FirestoreDb.CreateAsync("misskey-automations"))

        let! note =
          try
            NoteRecord.note (logger, db) note
          with ex ->
            logger.LogDebug($"Failed to retreive notes", ex)
            Task.FromResult(None)

        match note with
        | Some note ->
          response.ContentType <- "text/html;charset=utf-8"
          response.StatusCode <- 200
          return! Render.Note note |> response.WriteAsync
        | None ->
          response.StatusCode <- 404
          return! response.WriteAsync "Not Found"
    }