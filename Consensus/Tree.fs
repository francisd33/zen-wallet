﻿module Consensus.Tree

type LazyTree<'LeafData,'BranchData> =
    | Leaf of 'LeafData
    | Branch of 'BranchData * Lazy<LazyTree<'LeafData,'BranchData>> * Lazy<LazyTree<'LeafData,'BranchData>>

let rec lazyCata fLeaf fBranch (tree : LazyTree<'LeafData, 'BranchData>) : 'r =
    let recurse = lazyCata fLeaf fBranch
    let lazyRecurse (lTree : Lazy<_>) = lazy(recurse <| lTree.Force())
    match tree with
    | Leaf leafData -> fLeaf leafData
    | Branch (branchData, lazyTreeL, lazyTreeR) ->
        fBranch branchData <|| (lazyRecurse lazyTreeL, lazyRecurse lazyTreeR)

let lazyMap fLeaf fBranch =
    lazyCata (Leaf << fLeaf)
         (fun branchData lazyTreeL lazyTreeR ->
             Branch (fBranch branchData, lazyTreeL, lazyTreeR))

type FullTree<'L,'B> =
    | Leaf of 'L
    | Branch of data : 'B * left : FullTree<'L,'B> * right : FullTree<'L,'B>

let rec cata fLeaf fBranch (tree : FullTree<'L,'B>) =
    let recurse = cata fLeaf fBranch
    match tree with
    | Leaf leafData -> fLeaf leafData
    | Branch (branchData, leftTree, rightTree) ->
        fBranch branchData
        <| recurse leftTree
        <| recurse rightTree

let map fLeaf fBranch =
    cata (Leaf << fLeaf)
         (fun branchData treeL treeR ->
             Branch (fBranch branchData, treeL, treeR))


let rec height = function
    | Leaf _ -> 0
    | Branch (left=left) -> 1 + height left

type LocBranchData<'T> = {branchData:'T; height:int32; loc:uint32}
type LocLeafData<'T> = {leafData:'T; loc:uint32}


type Loc = {height:int32; loc:uint32}
type LocData<'T> = {data:'T; location:Loc}


let addLocation height tree =
    let right = function
        | {Loc.height=h;loc=l} -> {height = h-1; loc = l ||| (1u <<< (h - 1))}
    let left = function
        | {Loc.height=h; loc=l} -> {height = h-1; loc=l}
    let rec inner = function
        | location, Leaf v -> Leaf {data=v; location=location}
        | {height=height;loc=loc} as location, Branch (branchData, leftTree, rightTree) ->
            Branch <|
            (
                {data=branchData;location=location},
                inner (left location, leftTree),
                inner (right location, rightTree)
            )
    inner ({height=height;loc=0u}, tree)

let locLocation<'L,'B> : (FullTree<LocData<'L>,LocData<'B>> -> Loc) = function
    | Leaf v -> v.location
    | Branch (data=data) -> data.location

let locHeight<'L,'B> : (FullTree<LocData<'L>,LocData<'B>> -> int32) = function
    | Leaf v -> v.location.height
    | Branch (data=data) -> data.location.height

let locLoc<'L,'B> : (FullTree<LocData<'L>,LocData<'B>> -> uint32) = function
    | Leaf v -> v.location.loc
    | Branch (data=data) -> data.location.loc

type OptTree<'L,'B> = FullTree<'L option,'B>

let complete (s:seq<'T>) =
    let defaultB = {data=None; location={height=0;loc=0u}}
    let defaultHeight n = {data=None; location={height=n;loc=0u}}
    let leaves = Seq.map (addLocation 0 << Leaf << Some) s
    let rec parent stack a = 
        match stack, a with
        | y::tl, x when locHeight y = locHeight x -> parent tl (Branch (defaultHeight <| locHeight x + 1,y,x))
        | _, _ -> a :: stack
    let rec withEmptyNodes l =
        let defaultLeaf = Leaf defaultB
        let rec insertOne h tree =
            match locHeight tree with
            | n when n=h -> tree
            | n ->
                insertOne h
                <| Branch
                   (
                   defaultHeight <| n + 1,
                   tree,
                   Leaf <| defaultHeight n
                   )
        let group left right =
            match locHeight left, locHeight right with
            | greater, lesser ->
                Branch (
                    defaultHeight <| greater+1,
                    left,
                    insertOne greater right
                       )
        match l with
        | [] -> defaultLeaf
        | x::[] -> x
        | x::y::tl -> withEmptyNodes <| (group y x) :: tl
    let treeWithoutLoc = withEmptyNodes << Seq.fold parent [] <| leaves
    let stripLocation = map (fun v -> v.data) (fun v -> v.data)
    addLocation <| locHeight treeWithoutLoc <| stripLocation treeWithoutLoc

let normalize<'L,'B> =
    let fBranch (brData:LocData<'B>) tL tR =
        match tL, tR with
        | Leaf {data=None:'L option}, Leaf {data=None} -> Leaf {data=None; location=brData.location}
        | _ , _ -> Branch (brData, tL, tR)
    cata (Leaf << id) fBranch